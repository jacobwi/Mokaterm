// Syntax colouring for the remote file editor. Nothing in here touches the DOM, so it runs outside a browser as well.
//
// A tokenizer reads one line and returns the ranges it coloured, as [from, to, type], plus the state the next line
// starts in. States are short strings ('' when nothing is open), which is how a block comment or a triple-quoted string
// reaches past the end of its line, and how the model at the bottom tells with === that a line it read again ends the
// way it did before. Types become class names (tk-comment, tk-string, ...) on the page.
//
// The language ids are the ones EditorLanguages.cs lists; an id with no tokenizer here is plain text.

// A longer line is left plain and hands its state on unchanged. A minified bundle or a base64 blob on one line would
// otherwise be read again in full on every keystroke made on it.
const maxLineLength = 4000;

/** Reads one line left to right and collects coloured ranges, joining a range onto the last when they touch. */
class Scanner {
	constructor(text) {
		this.text = text;
		this.pos = 0;
		this.tokens = [];
	}

	get done() {
		return this.pos >= this.text.length;
	}

	/** The match of the sticky regex `re` at the current position, or null. Does not move. */
	at(re) {
		re.lastIndex = this.pos;
		const match = re.exec(this.text);
		return match ? match[0] : null;
	}

	/** Colours a non-empty match of `re` as `type` and moves past it. */
	take(re, type) {
		const match = this.at(re);
		if (!match) {
			return false;
		}

		this.mark(this.pos, this.pos + match.length, type);
		this.pos += match.length;
		return true;
	}

	mark(from, to, type) {
		// The page draws ranges in order, so one that reaches back into the last is cut to start where that one ended.
		const last = this.tokens[this.tokens.length - 1];
		if (last && from < last[1]) {
			from = last[1];
		}

		if (!type || to <= from) {
			return;
		}

		if (last && last[1] === from && last[2] === type) {
			last[1] = to;
			return;
		}

		this.tokens.push([from, to, type]);
	}

	rest(type) {
		this.mark(this.pos, this.text.length, type);
		this.pos = this.text.length;
	}

	skipSpace() {
		while (this.pos < this.text.length && (this.text[this.pos] === ' ' || this.text[this.pos] === '\t')) {
			this.pos++;
		}
	}

	/** Copies another tokenizer's ranges for a slice of this line that started at `offset`. */
	embed(tokens, offset) {
		for (const [from, to, type] of tokens) {
			this.mark(from + offset, to + offset, type);
		}
	}

	result(state) {
		return { tokens: this.tokens, state };
	}
}

/**
 * Reads to `close` and colours everything from the current position as `type`, the opening quote included when the
 * scanner stands on it. Returns false when the line ran out first. With `escapes` a backslash protects what follows.
 */
function quoted(s, close, type, escapes, opening = close) {
	const start = s.pos;
	if (opening && s.text.startsWith(opening, s.pos)) {
		s.pos += opening.length;
	}

	const text = s.text;
	while (s.pos < text.length) {
		if (escapes && text[s.pos] === '\\') {
			s.pos += 2;
			continue;
		}

		if (text.startsWith(close, s.pos)) {
			s.pos += close.length;
			s.mark(start, s.pos, type);
			return true;
		}

		s.pos++;
	}

	s.pos = text.length;
	s.mark(start, s.pos, type);
	return false;
}

/** A block comment from the current position (on its opener or inside it) to `close`. False when it stays open. */
function blockComment(s, close, opener) {
	const start = s.pos;
	const from = opener && s.text.startsWith(opener, s.pos) ? s.pos + opener.length : s.pos;
	const end = s.text.indexOf(close, from);
	if (end < 0) {
		s.pos = start;
		s.rest('comment');
		return false;
	}

	s.pos = end + close.length;
	s.mark(start, s.pos, 'comment');
	return true;
}

function isWordChar(c) {
	return c !== undefined && /[\p{L}\p{N}_$]/u.test(c);
}

function wordStart(s) {
	return s.pos === 0 || !isWordChar(s.text[s.pos - 1]);
}

function words(list) {
	return new Set(list.split(/\s+/).filter((word) => word.length > 0));
}

// Shell ---------------------------------------------------------------------------------------------------------------

const shellKeywords = words('if then else elif fi for while until do done case esac in function select time coproc');
const shellBuiltins = words(`alias bg bind break builtin caller cd command compgen complete continue declare dirs disown
	echo enable eval exec exit export false fc fg getopts hash help history jobs kill let local logout mapfile popd printf
	pushd pwd read readarray readonly return set shift shopt source suspend test times trap true type typeset ulimit umask
	unalias unset wait`);
const shellVariable = /\$(?:\{[^}]*\}?|[A-Za-z_][A-Za-z0-9_]*|[0-9@#?$!*-])/y;
const shellAssignment = /[A-Za-z_][A-Za-z0-9_]*(?=\+?=)/y;
const shellNumber = /\d+(?![^\s;&|<>()])/y;
const shellWord = /[^\s'"`$;&|<>(){}[\]\\=]+/y;
const shellOperator = /[;&|<>(){}[\]=!]+/y;
const heredocStart = /<<(-)?[ \t]*(['"]?)([A-Za-z0-9_][\w.-]*)\2/y;

/** A double-quoted string, with the variables inside it coloured as variables. */
function shellDouble(s, opening) {
	let start = s.pos;
	if (opening) {
		s.pos++;
	}

	const text = s.text;
	while (s.pos < text.length) {
		const c = text[s.pos];
		if (c === '\\') {
			s.pos += 2;
			continue;
		}

		if (c === '"') {
			s.pos++;
			s.mark(start, s.pos, 'string');
			return true;
		}

		if (c === '$') {
			const variable = s.at(shellVariable);
			if (variable) {
				s.mark(start, s.pos, 'string');
				s.mark(s.pos, s.pos + variable.length, 'variable');
				s.pos += variable.length;
				start = s.pos;
				continue;
			}
		}

		s.pos++;
	}

	s.pos = text.length;
	s.mark(start, s.pos, 'string');
	return false;
}

// States: '' | a quote that is still open (' " `) | 'h' + ('-' or ' ') + delimiter inside a here-document.
function shell(text, state) {
	const s = new Scanner(text);
	if (state.startsWith('h')) {
		const delimiter = state.slice(2);
		const line = state[1] === '-' ? text.replace(/^\t+/, '') : text;
		if (line === delimiter) {
			s.rest('meta');
			return s.result('');
		}

		s.rest('string');
		return s.result(state);
	}

	if (state === "'" && !quoted(s, "'", 'string', false, '')) {
		return s.result(state);
	}

	if (state === '`' && !quoted(s, '`', 'string', true, '')) {
		return s.result(state);
	}

	if (state === '"' && !shellDouble(s, false)) {
		return s.result(state);
	}

	let heredoc = '';
	while (!s.done) {
		const c = text[s.pos];
		if (c === ' ' || c === '\t') {
			s.pos++;
			continue;
		}

		const boundary = s.pos === 0 || /[\s;&|(){}]/.test(text[s.pos - 1]);
		if (c === '#' && boundary) {
			s.rest('comment');
			break;
		}

		if (c === "'") {
			if (!quoted(s, "'", 'string', false)) {
				return s.result("'");
			}

			continue;
		}

		if (c === '"') {
			if (!shellDouble(s, true)) {
				return s.result('"');
			}

			continue;
		}

		if (c === '`') {
			if (!quoted(s, '`', 'string', true)) {
				return s.result('`');
			}

			continue;
		}

		if (c === '$') {
			if (text[s.pos + 1] === "'") {
				s.mark(s.pos, s.pos + 1, 'string');
				s.pos++;
				if (!quoted(s, "'", 'string', true)) {
					return s.result("'");
				}

				continue;
			}

			if (s.take(shellVariable, 'variable') || s.take(/\$\(\(?/y, 'punct')) {
				continue;
			}

			s.pos++;
			continue;
		}

		if (c === '<' && text.startsWith('<<<', s.pos)) {
			s.mark(s.pos, s.pos + 3, 'punct');
			s.pos += 3;
			continue;
		}

		if (c === '<' && text[s.pos + 1] === '<') {
			heredocStart.lastIndex = s.pos;
			const match = heredocStart.exec(text);
			if (match) {
				const operatorEnd = s.pos + 2 + (match[1] ? 1 : 0);
				s.mark(s.pos, operatorEnd, 'punct');
				s.mark(operatorEnd, s.pos + match[0].length, 'meta');
				if (!heredoc) {
					heredoc = `h${match[1] ? '-' : ' '}${match[3]}`;
				}

				s.pos += match[0].length;
				continue;
			}
		}

		if (c === '\\') {
			s.mark(s.pos, Math.min(s.pos + 2, text.length), 'punct');
			s.pos += 2;
			continue;
		}

		if (boundary && s.take(shellAssignment, 'key')) {
			continue;
		}

		if ((boundary || text[s.pos - 1] === '=') && s.take(shellNumber, 'number')) {
			continue;
		}

		const word = s.at(shellWord);
		if (word) {
			const type = !boundary ? null : shellKeywords.has(word) ? 'keyword' : shellBuiltins.has(word) ? 'builtin' : null;
			s.mark(s.pos, s.pos + word.length, type);
			s.pos += word.length;
			continue;
		}

		if (s.take(shellOperator, 'punct')) {
			continue;
		}

		s.pos++;
	}

	return s.result(heredoc);
}

// JSON ----------------------------------------------------------------------------------------------------------------

const jsonNumber = /-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?/y;
const jsonKeyFollows = /\s*:/y;

// States: '' | '*' inside a block comment (JSON with comments, as tsconfig and VS Code settings are).
function json(text, state) {
	const s = new Scanner(text);
	if (state === '*' && !blockComment(s, '*/', '')) {
		return s.result('*');
	}

	while (!s.done) {
		const c = text[s.pos];
		if (c === ' ' || c === '\t') {
			s.pos++;
			continue;
		}

		if (c === '"') {
			const start = s.pos;
			quoted(s, '"', null, true);
			jsonKeyFollows.lastIndex = s.pos;
			s.mark(start, s.pos, jsonKeyFollows.test(text) ? 'key' : 'string');
			continue;
		}

		if (c === '/' && text[s.pos + 1] === '/') {
			s.rest('comment');
			break;
		}

		if (c === '/' && text[s.pos + 1] === '*') {
			if (!blockComment(s, '*/', '/*')) {
				return s.result('*');
			}

			continue;
		}

		if (s.take(jsonNumber, 'number') || s.take(/(?:true|false|null)\b/y, 'keyword') || s.take(/[{}[\],:]/y, 'punct')) {
			continue;
		}

		s.pos++;
	}

	return s.result('');
}

// YAML ----------------------------------------------------------------------------------------------------------------

const yamlKey = /(?:"(?:[^"\\]|\\.)*"|'(?:[^']|'')*'|[^\s#'"[\]{},&*!|>%@`-](?:[^:#]|:(?![ \t]|$)|(?<=\S)#)*?|-(?=\S)(?:[^:#]|:(?![ \t]|$)|(?<=\S)#)*?)(?=[ \t]*:(?:[ \t]|$))/y;
const yamlScalar = /[^\s#,[\]{}](?:[^\s,[\]{}]|[ \t]+(?![ \t]*(?:#|$)))*/y;
const yamlNumber = /^(?:[-+]?(?:\d[\d_]*(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?|0x[0-9a-fA-F]+|0o[0-7]+|[-+]?\.(?:inf|Inf|INF)|\.(?:nan|NaN|NAN))$/;
const yamlConstants = words('true false yes no on off null True False Yes No On Off Null TRUE FALSE YES NO ON OFF NULL ~');
const yamlBlock = /[|>][-+]?\d?[-+]?(?=[ \t]*(?:#|$))/y;

/** The value part of a YAML line, after its key or dash. Returns the state for a block scalar opened here. */
function yamlValue(s, indent) {
	const text = s.text;
	while (!s.done) {
		const c = text[s.pos];
		if (c === ' ' || c === '\t') {
			s.pos++;
			continue;
		}

		if (c === '#' && (s.pos === 0 || /\s/.test(text[s.pos - 1]))) {
			s.rest('comment');
			break;
		}

		if (c === '"') {
			quoted(s, '"', 'string', true);
			continue;
		}

		if (c === "'") {
			quoted(s, "'", 'string', false);
			continue;
		}

		if ((c === '&' || c === '*') && s.take(/[&*][^\s,[\]{}]+/y, 'variable')) {
			continue;
		}

		if (c === '!' && s.take(/!\S*/y, 'meta')) {
			continue;
		}

		if (s.take(yamlBlock, 'punct')) {
			s.skipSpace();
			if (text[s.pos] === '#') {
				s.rest('comment');
			}

			return `|${indent}`;
		}

		if (s.take(/[[\]{},:?-]/y, 'punct')) {
			continue;
		}

		const scalar = s.at(yamlScalar);
		if (scalar) {
			const type = yamlNumber.test(scalar) ? 'number' : yamlConstants.has(scalar) ? 'keyword' : 'string';
			s.mark(s.pos, s.pos + scalar.length, type);
			s.pos += scalar.length;
			continue;
		}

		s.pos++;
	}

	return '';
}

// States: '' | '|' + the indent of the line that opened a block scalar, whose deeper lines are all text.
function yaml(text, state) {
	const s = new Scanner(text);
	const indent = text.search(/\S/);
	if (state.startsWith('|')) {
		if (indent < 0) {
			return s.result(state);
		}

		if (indent > Number(state.slice(1))) {
			s.rest('string');
			return s.result(state);
		}
	}

	if (indent < 0) {
		return s.result('');
	}

	s.pos = indent;
	if (indent === 0 && /^(?:---|\.\.\.)(?:\s|$)/.test(text)) {
		s.mark(0, 3, 'meta');
		s.pos = 3;
		return s.result(yamlValue(s, -1));
	}

	if (text[s.pos] === '#') {
		s.rest('comment');
		return s.result('');
	}

	if (text[s.pos] === '%') {
		s.rest('meta');
		return s.result('');
	}

	// A list item may carry a key of its own ("- name: web"), and a block scalar under that key is indented past the
	// key rather than past the dash.
	let owner = indent;
	while (text[s.pos] === '-' && (s.pos + 1 === text.length || /\s/.test(text[s.pos + 1]))) {
		s.mark(s.pos, s.pos + 1, 'punct');
		s.pos++;
		s.skipSpace();
	}

	const keyAt = s.pos;
	if (s.take(yamlKey, 'key')) {
		owner = keyAt;
		s.skipSpace();
		s.take(/:/y, 'punct');
	}

	return s.result(yamlValue(s, owner));
}

// INI, config files, TOML ---------------------------------------------------------------------------------------------

const iniKey = /[^\s=:#;[\]"'][^\s=:#;"']*(?=[ \t]*[=:])/y;
const iniFirstWord = /[^\s=#;"']+/y;
const tomlKeyPart = String.raw`(?:[A-Za-z0-9_-]+|"(?:[^"\\]|\\.)*"|'[^']*')`;
const tomlKey = new RegExp(String.raw`${tomlKeyPart}(?:[ \t]*\.[ \t]*${tomlKeyPart})*(?=[ \t]*=)`, 'y');
const iniNumber = /[-+]?(?:0x[0-9a-fA-F_]+|0o[0-7_]+|0b[01_]+|\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[-+]\d{2}:\d{2})?)?|\d[\d_]*(?:\.\d+)?(?:[eE][-+]?\d+)?)(?![\w.:-])/y;
const iniConstant = /(?:true|false|yes|no|on|off|none|null)(?![\w.-])/iy;
const iniVariable = /\$\{[^}]*\}|\$[A-Za-z_]\w*|%\([^)]*\)[sdif]?|%[A-Za-z%]/y;

/** The value of an INI or TOML line. Returns the quote of a TOML multi-line string left open. */
function iniValue(s, toml) {
	const text = s.text;
	while (!s.done) {
		const c = text[s.pos];
		if (c === ' ' || c === '\t') {
			s.pos++;
			continue;
		}

		const spaced = s.pos === 0 || /\s/.test(text[s.pos - 1]);
		if (c === '#' && (spaced || toml)) {
			s.rest('comment');
			break;
		}

		if (c === ';' && spaced && !toml) {
			s.rest('comment');
			break;
		}

		if (toml && (text.startsWith('"""', s.pos) || text.startsWith("'''", s.pos))) {
			const quote = text.slice(s.pos, s.pos + 3);
			if (!quoted(s, quote, 'string', quote === '"""')) {
				return quote;
			}

			continue;
		}

		if (c === '"' || c === "'") {
			quoted(s, c, 'string', c === '"');
			continue;
		}

		if (wordStart(s) && (s.take(iniNumber, 'number') || s.take(iniConstant, 'keyword'))) {
			continue;
		}

		if ((c === '$' || c === '%') && s.take(iniVariable, 'variable')) {
			continue;
		}

		if (s.take(/[[\]{},=]/y, 'punct')) {
			continue;
		}

		if (s.take(/[^\s"'#;,[\]{}=$%]+/y, null)) {
			continue;
		}

		s.pos++;
	}

	return '';
}

// States: '' | the quote of a TOML multi-line string still open.
function ini(text, state, toml) {
	const s = new Scanner(text);
	if (state) {
		if (!quoted(s, state, 'string', state === '"""', '')) {
			return s.result(state);
		}

		return s.result(iniValue(s, toml));
	}

	s.skipSpace();
	const c = text[s.pos];
	if (c === '#' || (c === ';' && !toml)) {
		s.rest('comment');
		return s.result('');
	}

	if (c === '[' && s.take(/\[\[?[^\]]*\]\]?/y, 'meta')) {
		return s.result(iniValue(s, toml));
	}

	if (toml) {
		if (s.take(tomlKey, 'key')) {
			s.skipSpace();
			s.take(/=/y, 'punct');
		}
	}
	else if (s.take(iniKey, 'key')) {
		s.skipSpace();
		s.take(/[=:]/y, 'punct');
	}
	else {
		// A config file in "Keyword value" form, the way sshd_config and most of /etc is written.
		s.take(iniFirstWord, 'key');
	}

	return s.result(iniValue(s, toml));
}

// XML and HTML --------------------------------------------------------------------------------------------------------

const markupTag = /<(\/?)([A-Za-z_][\w:.-]*)/y;
const markupEntity = /&(?:#\d+|#x[0-9a-fA-F]+|[A-Za-z][\w]*);/g;

function markupText(s, end) {
	markupEntity.lastIndex = s.pos;
	for (let match = markupEntity.exec(s.text); match && match.index < end; match = markupEntity.exec(s.text)) {
		s.mark(match.index, match.index + match[0].length, 'variable');
	}

	s.pos = end;
}

// States: '' | 'c' comment | 'd' CDATA | '?' declaration | 't' + tag (inside a tag) | 'v' + quote + tag state (inside an
// attribute value) | 'r' + script or style + ':' + the embedded language's own state (HTML only).
function markup(text, state, html) {
	const s = new Scanner(text);
	let mode = state;
	while (!s.done) {
		if (mode === '') {
			const open = text.indexOf('<', s.pos);
			markupText(s, open < 0 ? text.length : open);
			if (open < 0) {
				break;
			}

			if (text.startsWith('<!--', s.pos)) {
				mode = 'c';
				s.mark(s.pos, s.pos + 4, 'comment');
				s.pos += 4;
				continue;
			}

			if (text.startsWith('<![CDATA[', s.pos)) {
				mode = 'd';
				s.mark(s.pos, s.pos + 9, 'meta');
				s.pos += 9;
				continue;
			}

			if (text.startsWith('<!', s.pos) || text.startsWith('<?', s.pos)) {
				mode = '?';
				continue;
			}

			markupTag.lastIndex = s.pos;
			const tag = markupTag.exec(text);
			if (tag) {
				const nameStart = s.pos + 1 + tag[1].length;
				s.mark(s.pos, nameStart, 'punct');
				s.mark(nameStart, s.pos + tag[0].length, 'tag');
				s.pos += tag[0].length;
				mode = `t${tag[1]}${tag[2].toLowerCase()}`;
				continue;
			}

			s.pos++;
			continue;
		}

		if (mode === 'c' || mode === 'd' || mode === '?') {
			const close = mode === 'c' ? '-->' : mode === 'd' ? ']]>' : '>';
			const end = text.indexOf(close, s.pos);
			const type = mode === 'c' ? 'comment' : mode === 'd' ? 'string' : 'meta';
			if (end < 0) {
				s.rest(type);
				break;
			}

			s.mark(s.pos, end, type);
			s.mark(end, end + close.length, mode === 'd' ? 'meta' : type);
			s.pos = end + close.length;
			mode = '';
			continue;
		}

		if (mode[0] === 'v') {
			const quote = mode[1];
			const end = text.indexOf(quote, s.pos);
			if (end < 0) {
				s.rest('string');
				break;
			}

			s.mark(s.pos, end + 1, 'string');
			s.pos = end + 1;
			mode = mode.slice(2);
			continue;
		}

		if (mode[0] === 'r') {
			const colon = mode.indexOf(':');
			const name = mode.slice(1, colon);
			const close = text.toLowerCase().indexOf(`</${name}`, s.pos);
			const end = close < 0 ? text.length : close;
			const inner = tokenizeLine(name === 'script' ? 'javascript' : 'css', text.slice(s.pos, end), mode.slice(colon + 1));
			s.embed(inner.tokens, s.pos);
			s.pos = end;
			mode = close < 0 ? `r${name}:${inner.state}` : '';
			continue;
		}

		// Inside a tag: attribute names, values and the end of the tag.
		const c = text[s.pos];
		if (c === ' ' || c === '\t') {
			s.pos++;
			continue;
		}

		if (text.startsWith('/>', s.pos) || text.startsWith('?>', s.pos)) {
			s.mark(s.pos, s.pos + 2, 'punct');
			s.pos += 2;
			mode = '';
			continue;
		}

		if (c === '>') {
			s.mark(s.pos, s.pos + 1, 'punct');
			s.pos++;
			const name = mode.slice(1);
			mode = html && (name === 'script' || name === 'style') ? `r${name}:` : '';
			continue;
		}

		if (c === '"' || c === "'") {
			if (!quoted(s, c, 'string', false)) {
				mode = `v${c}${mode}`;
				break;
			}

			continue;
		}

		if (s.take(/[^\s=/>"']+/y, 'attr') || s.take(/[=/]/y, 'punct')) {
			continue;
		}

		s.pos++;
	}

	return s.result(mode);
}

// nginx ---------------------------------------------------------------------------------------------------------------

const nginxNumber = /^\d+(?:\.\d+)*(?::\d+)?(?:ms|[kKmMgGsShHdDwWyY])?$/;
const nginxModifiers = words('= ~ ~* ^~');

// States: '' at the start of a directive | 'a' among its arguments, which may run onto the next line.
function nginx(text, state) {
	const s = new Scanner(text);
	let directive = state !== 'a';
	while (!s.done) {
		const c = text[s.pos];
		if (c === ' ' || c === '\t') {
			s.pos++;
			continue;
		}

		if (c === '#' && (s.pos === 0 || /[\s;{}]/.test(text[s.pos - 1]))) {
			s.rest('comment');
			break;
		}

		if (c === '"' || c === "'") {
			quoted(s, c, 'string', true);
			directive = false;
			continue;
		}

		if (c === '$' && s.take(/\$\{?[A-Za-z_]\w*\}?/y, 'variable')) {
			directive = false;
			continue;
		}

		if (c === ';' || c === '{' || c === '}') {
			s.mark(s.pos, s.pos + 1, 'punct');
			s.pos++;
			directive = true;
			continue;
		}

		const word = s.at(/[^\s;{}"'$]+/y);
		if (word) {
			const type = directive ? 'keyword'
				: nginxModifiers.has(word) ? 'punct'
				: word === 'on' || word === 'off' ? 'builtin'
				: nginxNumber.test(word) ? 'number'
				: null;
			s.mark(s.pos, s.pos + word.length, type);
			s.pos += word.length;
			directive = false;
			continue;
		}

		s.pos++;
	}

	return s.result(directive ? '' : 'a');
}

// Dockerfile ----------------------------------------------------------------------------------------------------------

const dockerInstructions = words(`ADD ARG CMD COPY ENTRYPOINT ENV EXPOSE FROM HEALTHCHECK LABEL MAINTAINER ONBUILD RUN SHELL
	STOPSIGNAL USER VOLUME WORKDIR`);

// States: '' | 'c' + the shell's own state while an instruction carries on past a trailing backslash or an open quote.
function dockerfile(text, state) {
	const s = new Scanner(text);
	let shellState = '';
	if (state.startsWith('c')) {
		shellState = state.slice(1);
		if (shellState === '' && /^\s*(?:#|$)/.test(text)) {
			// Docker drops comment and empty lines inside a continued instruction, and carries on after them.
			s.skipSpace();
			s.rest('comment');
			return s.result(state);
		}
	}
	else {
		s.skipSpace();
		if (text[s.pos] === '#') {
			s.rest(/^#\s*(?:syntax|escape|check)\s*=/i.test(text) ? 'meta' : 'comment');
			return s.result('');
		}

		const instruction = s.at(/[A-Za-z]+/y);
		if (instruction && dockerInstructions.has(instruction.toUpperCase())) {
			s.mark(s.pos, s.pos + instruction.length, 'keyword');
			s.pos += instruction.length;
			if (instruction.toUpperCase() === 'FROM') {
				const stage = /^(.*?\s)(as)(\s+)(\S+)/i.exec(text.slice(s.pos));
				if (stage) {
					s.embed(shell(stage[1], '').tokens, s.pos);
					const at = s.pos + stage[1].length;
					s.mark(at, at + 2, 'keyword');
					s.mark(at + 2 + stage[3].length, at + 2 + stage[3].length + stage[4].length, 'key');
					return s.result('');
				}
			}
		}
	}

	const rest = shell(text.slice(s.pos), shellState);
	s.embed(rest.tokens, s.pos);
	const continues = /\\[ \t]*$/.test(text) || rest.state !== '';
	return s.result(continues ? `c${rest.state}` : '');
}

// Python --------------------------------------------------------------------------------------------------------------

const pythonKeywords = words(`False None True and as assert async await break case class continue def del elif else except
	finally for from global if import in is lambda match nonlocal not or pass raise return try type while with yield`);
const pythonBuiltins = words(`abs all any ascii bin bool breakpoint bytearray bytes callable chr classmethod compile complex
	delattr dict dir divmod enumerate eval exec filter float format frozenset getattr globals hasattr hash help hex id input
	int isinstance issubclass iter len list locals map max memoryview min next object oct open ord pow print property range
	repr reversed round set setattr slice sorted staticmethod str sum super tuple vars zip self cls __name__ __file__
	Exception ValueError TypeError KeyError IndexError RuntimeError OSError StopIteration NotImplementedError
	AttributeError ImportError`);
const pythonString = /[rRbBuUfF]{0,2}("""|'''|"|')/y;
const pythonNumber = /(?:0[xX][\da-fA-F_]+|0[oO][0-7_]+|0[bB][01_]+|(?:\d[\d_]*\.?[\d_]*|\.\d[\d_]*)(?:[eE][-+]?\d+)?[jJ]?)/y;
const identifier = /[\p{L}_$][\p{L}\p{N}_$]*/uy;

// States: '' | the triple quote of a string still open.
function python(text, state) {
	const s = new Scanner(text);
	if (state && !quoted(s, state, 'string', true, '')) {
		return s.result(state);
	}

	let naming = false;
	while (!s.done) {
		const c = text[s.pos];
		if (c === ' ' || c === '\t') {
			s.pos++;
			continue;
		}

		if (c === '#') {
			s.rest('comment');
			break;
		}

		if (c === '@' && text.slice(0, s.pos).trim() === '' && s.take(/@[\w.]+/y, 'meta')) {
			continue;
		}

		if (wordStart(s)) {
			pythonString.lastIndex = s.pos;
			const string = pythonString.exec(text);
			if (string) {
				// The prefix first, so the string that follows joins onto it.
				const prefix = string[0].length - string[1].length;
				s.mark(s.pos, s.pos + prefix, 'string');
				s.pos += prefix;
				const closed = quoted(s, string[1], 'string', true);
				if (!closed && string[1].length === 3) {
					return s.result(string[1]);
				}

				continue;
			}

			if (/\d|\./.test(c) && s.take(pythonNumber, 'number')) {
				continue;
			}
		}

		const name = s.at(identifier);
		if (name) {
			const type = naming ? 'key' : pythonKeywords.has(name) ? 'keyword' : pythonBuiltins.has(name) ? 'builtin' : null;
			naming = name === 'def' || name === 'class';
			s.mark(s.pos, s.pos + name.length, type);
			s.pos += name.length;
			continue;
		}

		if (s.take(/[()[\]{}:,.;=+\-*/%<>!&|^~@]+/y, 'punct')) {
			naming = false;
			continue;
		}

		s.pos++;
	}

	return s.result('');
}

// JavaScript and TypeScript -------------------------------------------------------------------------------------------

const scriptKeywords = words(`async await break case catch class const continue debugger default delete do else export
	extends false finally for from function get if import in instanceof let new null of return set static super switch
	this throw true try typeof undefined var void while with yield`);
const typeKeywords = words(`abstract any as asserts bigint boolean declare enum implements infer interface is keyof module
	namespace never number object private protected public readonly satisfies string symbol type unique unknown`);
const scriptBuiltins = words(`Array ArrayBuffer BigInt Boolean console Date document Error globalThis JSON Map Math Number
	Object Promise Proxy Reflect RegExp Set String Symbol URL WeakMap WeakSet window process require module exports`);
const scriptNumber = /(?:0[xXoObB][\da-fA-F_]+n?|(?:\d[\d_]*\.?[\d_]*|\.\d[\d_]*)(?:[eE][-+]?\d+)?n?)/y;
const scriptRegex = /\/(?![*/])(?:\\.|\[(?:\\.|[^\]\\])*\]|[^/\\[])+\/[a-z]*/y;
const scriptValues = words('this super true false null undefined');

// States: '' | '*' inside a block comment | '`' inside a template literal.
function script(text, state, typescript) {
	const s = new Scanner(text);
	if (state === '*' && !blockComment(s, '*/', '')) {
		return s.result('*');
	}

	if (state === '`' && !quoted(s, '`', 'string', true, '')) {
		return s.result('`');
	}

	// Whether a slash here would start a regular expression rather than divide.
	let regex = true;
	let naming = false;
	while (!s.done) {
		const c = text[s.pos];
		if (c === ' ' || c === '\t') {
			s.pos++;
			continue;
		}

		if (c === '/' && text[s.pos + 1] === '/') {
			s.rest('comment');
			break;
		}

		if (c === '/' && text[s.pos + 1] === '*') {
			if (!blockComment(s, '*/', '/*')) {
				return s.result('*');
			}

			continue;
		}

		if (c === '"' || c === "'") {
			quoted(s, c, 'string', true);
			regex = false;
			continue;
		}

		if (c === '`') {
			if (!quoted(s, '`', 'string', true)) {
				return s.result('`');
			}

			regex = false;
			continue;
		}

		if (c === '/' && regex && s.take(scriptRegex, 'string')) {
			regex = false;
			continue;
		}

		if (wordStart(s) && /\d|\./.test(c) && /\d/.test(text[s.pos + (c === '.' ? 1 : 0)] ?? '') && s.take(scriptNumber, 'number')) {
			regex = false;
			continue;
		}

		if (c === '@' && s.take(/@[\w.]+/y, 'meta')) {
			continue;
		}

		const name = s.at(identifier);
		if (name) {
			const afterDot = s.pos > 0 && text[s.pos - 1] === '.';
			let type = null;
			if (naming) {
				type = 'key';
			}
			else if (!afterDot && (scriptKeywords.has(name) || (typescript && typeKeywords.has(name)))) {
				type = 'keyword';
			}
			else if (!afterDot && scriptBuiltins.has(name)) {
				type = 'builtin';
			}

			naming = !afterDot && (name === 'function' || name === 'class' || (typescript && (name === 'interface' || name === 'enum')));
			regex = type === 'keyword' && !scriptValues.has(name);
			s.mark(s.pos, s.pos + name.length, type);
			s.pos += name.length;
			continue;
		}

		const operator = s.at(/[{}()[\];,.<>=!+\-*%&|^~?:/]/y);
		if (operator) {
			s.mark(s.pos, s.pos + 1, 'punct');
			s.pos++;
			naming = naming && operator === '*';
			regex = !/[)\]}]/.test(operator);
			continue;
		}

		s.pos++;
	}

	return s.result('');
}

// CSS -----------------------------------------------------------------------------------------------------------------

// States: the brace depth, prefixed by 'c' while a block comment is open. The depth decides whether a name is a selector
// or a property, and it is capped so a broken file cannot grow the state without end.
function css(text, state) {
	const s = new Scanner(text);
	const inComment = state.startsWith('c');
	let depth = Number(inComment ? state.slice(1) : state) || 0;
	if (inComment && !blockComment(s, '*/', '')) {
		return s.result(state);
	}

	while (!s.done) {
		const c = text[s.pos];
		if (c === ' ' || c === '\t') {
			s.pos++;
			continue;
		}

		if (c === '/' && text[s.pos + 1] === '*') {
			if (!blockComment(s, '*/', '/*')) {
				return s.result(`c${depth}`);
			}

			continue;
		}

		// Sass and Less line comments. A url() is read whole below, so the // in an address never gets here.
		if (c === '/' && text[s.pos + 1] === '/' && (s.pos === 0 || /[\s;{}]/.test(text[s.pos - 1]))) {
			s.rest('comment');
			break;
		}

		if (c === '"' || c === "'") {
			quoted(s, c, 'string', true);
			continue;
		}

		if (c === '{' || c === '}') {
			s.mark(s.pos, s.pos + 1, 'punct');
			s.pos++;
			depth = c === '{' ? Math.min(depth + 1, 9) : Math.max(depth - 1, 0);
			continue;
		}

		if (c === '@' && s.take(/@[\w-]+/y, 'keyword')) {
			continue;
		}

		if (c === '!' && s.take(/!important\b/iy, 'keyword')) {
			continue;
		}

		if (c === '$' && s.take(/\$[\w-]+/y, 'variable')) {
			continue;
		}

		if (s.take(/url\([^)]*\)?/iy, 'string')) {
			continue;
		}

		if (c === '#') {
			if (depth > 0 && s.take(/#[0-9a-fA-F]{3,8}(?![\w-])/y, 'number')) {
				continue;
			}

			if (s.take(/#[\w-]+/y, 'attr')) {
				continue;
			}
		}

		if (c === '.' && !/\d/.test(text[s.pos + 1] ?? '') && s.take(/\.[\w-]+/y, 'attr')) {
			continue;
		}

		// Inside a block a colon starts a pseudo-class only on a nested selector, one that opens a block of its own.
		if (c === ':' && (depth === 0 || /^::?[\w-]+[^;{}]*\{/.test(text.slice(s.pos))) && s.take(/::?[\w-]+/y, 'builtin')) {
			continue;
		}

		if (wordStart(s) && /[\d.-]/.test(c) && s.take(/-?(?:\d+\.?\d*|\.\d+)(?:%|[a-zA-Z]+)?/y, 'number')) {
			continue;
		}

		const name = s.at(/-{0,2}[A-Za-z_][\w-]*/y);
		if (name) {
			const end = s.pos + name.length;
			let type = null;
			if (text[end] === '(') {
				type = 'builtin';
			}
			else if (depth > 0 && /^[ \t]*:(?!:)/.test(text.slice(end))) {
				type = 'key';
			}
			else if (name.startsWith('--')) {
				type = 'variable';
			}
			else if (depth === 0) {
				type = 'tag';
			}

			s.mark(s.pos, end, type);
			s.pos = end;
			continue;
		}

		if (s.take(/[;:,()>+~*=[\]|]/y, 'punct')) {
			continue;
		}

		s.pos++;
	}

	return s.result(String(depth));
}

// SQL -----------------------------------------------------------------------------------------------------------------

const sqlKeywords = words(`ADD ALL ALTER ANALYZE AND ANY AS ASC BEGIN BETWEEN BY CASCADE CASE CHECK COLUMN COMMIT CONFLICT
	CONSTRAINT CREATE CROSS CURRENT_DATE CURRENT_TIMESTAMP DATABASE DECLARE DEFAULT DELETE DESC DISTINCT DO DROP ELSE END
	ESCAPE EXCEPT EXISTS EXPLAIN FALSE FETCH FOR FOREIGN FROM FULL FUNCTION GRANT GROUP HAVING IF ILIKE IN INDEX INNER
	INSERT INTERSECT INTO IS JOIN KEY LANGUAGE LEFT LIKE LIMIT NATURAL NOT NOTHING NULL OFFSET ON OR ORDER OUTER OVER
	PARTITION PRIMARY PROCEDURE RECURSIVE REFERENCES RENAME REPLACE RETURNING RETURNS REVOKE RIGHT ROLLBACK ROW ROWS SCHEMA
	SELECT SEQUENCE SET SHOW TABLE TEMPORARY THEN TO TRANSACTION TRIGGER TRUE TRUNCATE UNION UNIQUE UPDATE USE USING VALUES
	VIEW WHEN WHERE WINDOW WITH`);
const sqlBuiltins = words(`AVG BIGINT BIGSERIAL BINARY BLOB BOOL BOOLEAN BYTEA CAST CHAR COALESCE CONCAT COUNT DATE
	DATETIME DECIMAL DOUBLE ENUM FLOAT INT INTEGER INTERVAL JSON JSONB LOWER MAX MIN NOW NULLIF NUMERIC REAL ROUND SERIAL
	SMALLINT SUBSTRING SUM TEXT TIME TIMESTAMP TIMESTAMPTZ TINYINT UPPER UUID VARCHAR`);

// States: '' | '*' inside a block comment | "'" inside a string that runs over the line.
function sql(text, state) {
	const s = new Scanner(text);
	if (state === '*' && !blockComment(s, '*/', '')) {
		return s.result('*');
	}

	if (state === "'" && !quoted(s, "'", 'string', false, '')) {
		return s.result("'");
	}

	while (!s.done) {
		const c = text[s.pos];
		if (c === ' ' || c === '\t') {
			s.pos++;
			continue;
		}

		if (c === '-' && text[s.pos + 1] === '-') {
			s.rest('comment');
			break;
		}

		if (c === '/' && text[s.pos + 1] === '*') {
			if (!blockComment(s, '*/', '/*')) {
				return s.result('*');
			}

			continue;
		}

		// A doubled quote inside a string is two strings that touch, which colour as one.
		if (c === "'") {
			if (!quoted(s, "'", 'string', false)) {
				return s.result("'");
			}

			continue;
		}

		if (c === '"' || c === '`') {
			quoted(s, c, 'key', false);
			continue;
		}

		if ((c === '$' || c === '@' || c === ':' || c === '?') && s.take(/(?:\$\d+|@@?[A-Za-z_]\w*|:[A-Za-z_]\w*|\?\d*)/y, 'variable')) {
			continue;
		}

		if (wordStart(s) && s.take(/\d+(?:\.\d+)?(?:[eE][-+]?\d+)?/y, 'number')) {
			continue;
		}

		const name = s.at(/[A-Za-z_][\w$]*/y);
		if (name) {
			const upper = name.toUpperCase();
			const type = sqlKeywords.has(upper) ? 'keyword' : sqlBuiltins.has(upper) ? 'builtin' : null;
			s.mark(s.pos, s.pos + name.length, type);
			s.pos += name.length;
			continue;
		}

		if (s.take(/[(),;.=<>!+\-*/%|&^~:]+/y, 'punct')) {
			continue;
		}

		s.pos++;
	}

	return s.result('');
}

// Markdown ------------------------------------------------------------------------------------------------------------

const fenceLanguages = new Map(Object.entries({
	sh: 'shell', bash: 'shell', shell: 'shell', zsh: 'shell', console: 'shell', env: 'shell',
	js: 'javascript', javascript: 'javascript', jsx: 'javascript', mjs: 'javascript',
	ts: 'typescript', typescript: 'typescript', tsx: 'typescript',
	py: 'python', python: 'python', python3: 'python',
	json: 'json', jsonc: 'json', yaml: 'yaml', yml: 'yaml', toml: 'toml', ini: 'ini', conf: 'ini', cfg: 'ini',
	xml: 'xml', svg: 'xml', html: 'html', htm: 'html', css: 'css', scss: 'css', less: 'css', sql: 'sql',
	nginx: 'nginx', dockerfile: 'dockerfile', docker: 'dockerfile', makefile: 'makefile', make: 'makefile',
	md: 'markdown', markdown: 'markdown',
}));

/** Code spans, links and comments inside an ordinary Markdown line. Returns 'h' for an HTML comment left open. */
function markdownInline(s) {
	const text = s.text;
	while (!s.done) {
		const c = text[s.pos];
		if (c === '`') {
			const run = s.at(/`+/y);
			const end = text.indexOf(run, s.pos + run.length);
			if (end < 0) {
				s.pos += run.length;
				continue;
			}

			s.mark(s.pos, end + run.length, 'string');
			s.pos = end + run.length;
			continue;
		}

		if (c === '<' && text.startsWith('<!--', s.pos)) {
			if (!blockComment(s, '-->', '<!--')) {
				return 'h';
			}

			continue;
		}

		if (c === '<' && s.take(/<(?:https?|ftp|mailto):[^\s>]+>/y, 'link')) {
			continue;
		}

		if ((c === '[' || c === '!') && s.take(/!?\[[^\]]*\](?:\([^)\s]*(?:\s+"[^"]*")?\)|\[[^\]]*\])/y, 'link')) {
			continue;
		}

		if (wordStart(s) && (c === 'h' || c === 'f') && s.take(/(?:https?|ftp):\/\/[^\s<>)\]]+/y, 'link')) {
			continue;
		}

		s.pos++;
	}

	return '';
}

// States: '' | 'h' inside an HTML comment | 'f' + fence + ':' + language + ':' + that language's state, inside a fenced
// code block, whose lines are coloured as the language its info string names.
function markdown(text, state) {
	const s = new Scanner(text);
	if (state.startsWith('f')) {
		const first = state.indexOf(':');
		const second = state.indexOf(':', first + 1);
		const fence = state.slice(1, first);
		const language = state.slice(first + 1, second);
		const closing = /^ {0,3}(`{3,}|~{3,})[ \t]*$/.exec(text);
		if (closing && closing[1][0] === fence[0] && closing[1].length >= fence.length) {
			s.rest('punct');
			return s.result('');
		}

		if (language) {
			const inner = tokenizeLine(language, text, state.slice(second + 1));
			s.embed(inner.tokens, 0);
			return s.result(`f${fence}:${language}:${inner.state}`);
		}

		s.rest('string');
		return s.result(state);
	}

	if (state === 'h' && !blockComment(s, '-->', '')) {
		return s.result('h');
	}

	if (s.pos === 0) {
		const fence = /^( {0,3})(`{3,}|~{3,})[ \t]*([^\s`]*)/.exec(text);
		if (fence) {
			const at = fence[1].length;
			s.mark(at, at + fence[2].length, 'punct');
			s.mark(at + fence[2].length, text.length, 'meta');
			const language = fenceLanguages.get(fence[3].toLowerCase()) ?? '';
			return s.result(`f${fence[2]}:${language}:`);
		}

		if (/^ {0,3}#{1,6}(?:\s|$)/.test(text) || /^ {0,3}=+[ \t]*$/.test(text)) {
			s.rest('heading');
			return s.result('');
		}

		if (/^ {0,3}([-*_])(?:[ \t]*\1){2,}[ \t]*$/.test(text)) {
			s.rest('punct');
			return s.result('');
		}

		if (s.take(/^ {0,3}\[[^\]]+\]:/y, 'link')) {
			s.rest('link');
			return s.result('');
		}

		s.take(/^(?:[ \t]*>)+[ \t]?/y, 'punct');
		const marker = /^([ \t]*)(?:[-*+]|\d{1,9}[.)])(?=[ \t]|$)/.exec(text.slice(s.pos));
		if (marker) {
			const at = s.pos + marker[1].length;
			s.mark(at, s.pos + marker[0].length, 'punct');
			s.pos += marker[0].length;
			s.skipSpace();
			s.take(/\[[ xX]\](?=[ \t])/y, 'punct');
		}
	}

	return s.result(markdownInline(s));
}

// Makefile ------------------------------------------------------------------------------------------------------------

const makeDirectives = words(`include -include sinclude ifeq ifneq ifdef ifndef else endif define endef export unexport
	override private undefine vpath`);
const makeVariable = /\$(?:\([^)]*\)?|\{[^}]*\}?|[@<^?*%+|]|\w)/y;

/** Variables, strings and comments in the rest of a make line. */
function makeRest(s, recipe) {
	const text = s.text;
	while (!s.done) {
		const c = text[s.pos];
		if (c === '$' && s.take(makeVariable, 'variable')) {
			continue;
		}

		if (c === '#' && (s.pos === 0 || /\s/.test(text[s.pos - 1]))) {
			s.rest('comment');
			break;
		}

		if (recipe && (c === '"' || c === "'")) {
			quoted(s, c, 'string', c === '"');
			continue;
		}

		if (c === '\\' && s.pos === text.length - 1) {
			s.mark(s.pos, s.pos + 1, 'punct');
		}

		s.pos++;
	}
}

// States: '' | 'r' a recipe line continued | 'v' an assignment continued | 'd' inside define ... endef.
function makefile(text, state) {
	const s = new Scanner(text);
	const continues = /\\$/.test(text);
	if (state === 'd') {
		if (s.take(/^[ \t]*endef\b/y, 'keyword')) {
			makeRest(s, false);
			return s.result('');
		}

		makeRest(s, false);
		return s.result('d');
	}

	// Before the recipe test: the continued value of an assignment is often indented with a tab as well.
	if (state === 'v') {
		makeRest(s, false);
		return s.result(continues ? 'v' : '');
	}

	if (state === 'r' || text.startsWith('\t')) {
		s.take(/^\t*[@+-]+/y, 'punct');
		makeRest(s, true);
		return s.result(continues ? 'r' : '');
	}

	s.skipSpace();
	if (text[s.pos] === '#') {
		s.rest('comment');
		return s.result('');
	}

	const directive = s.at(/-?[a-z]+/y);
	if (directive && makeDirectives.has(directive)) {
		s.mark(s.pos, s.pos + directive.length, 'keyword');
		s.pos += directive.length;
		if (directive === 'define') {
			s.skipSpace();
			s.take(/[^\s=]+/y, 'key');
			makeRest(s, false);
			return s.result('d');
		}

		s.skipSpace();
	}

	const assignment = /^([^\s:#=]+)[ \t]*(\?=|:::?=|::=|:=|\+=|!=|=)/.exec(text.slice(s.pos));
	if (assignment) {
		s.mark(s.pos, s.pos + assignment[1].length, 'key');
		const operatorAt = s.pos + assignment[0].length - assignment[2].length;
		s.mark(operatorAt, operatorAt + assignment[2].length, 'punct');
		s.pos += assignment[0].length;
		makeRest(s, false);
		return s.result(continues ? 'v' : '');
	}

	const rule = /^([^:#=]+?)(::?)(?!=)/.exec(text.slice(s.pos));
	if (rule) {
		s.mark(s.pos, s.pos + rule[1].length, 'key');
		s.mark(s.pos + rule[1].length, s.pos + rule[0].length, 'punct');
		s.pos += rule[0].length;
	}

	makeRest(s, false);
	return s.result(continues ? 'v' : '');
}

// The catalogue -------------------------------------------------------------------------------------------------------

const tokenizers = new Map([
	['shell', shell],
	['json', json],
	['yaml', yaml],
	['ini', (text, state) => ini(text, state, false)],
	['toml', (text, state) => ini(text, state, true)],
	['xml', (text, state) => markup(text, state, false)],
	['html', (text, state) => markup(text, state, true)],
	['nginx', nginx],
	['dockerfile', dockerfile],
	['python', python],
	['javascript', (text, state) => script(text, state, false)],
	['typescript', (text, state) => script(text, state, true)],
	['css', css],
	['sql', sql],
	['markdown', markdown],
	['makefile', makefile],
]);

/** The ids that colour anything. Everything else, 'plain' included, is shown as it is. */
export const languages = Object.freeze([...tokenizers.keys()]);

export function canHighlight(language) {
	return tokenizers.has(language);
}

/** Colours one line read in `state`. Returns { tokens: [[from, to, type], ...], state } for the next line. */
export function tokenizeLine(language, text, state = '') {
	const tokenizer = tokenizers.get(language);
	if (!tokenizer || text.length > maxLineLength) {
		return { tokens: [], state };
	}

	return tokenizer(text, state);
}

/**
 * The colours of a whole text, kept line by line so an edit is read again only from the first line it touched, and
 * only until a line starts in the same state it started in before: past that line nothing can read differently.
 */
export function createHighlightModel(language) {
	let lines = [];
	// starts[i] is the state line i starts in; the extra last entry is the state after the last line.
	let starts = [''];
	let tokens = [];

	return {
		get lineCount() {
			return lines.length;
		},

		lineAt(index) {
			return lines[index];
		},

		tokensAt(index) {
			return tokens[index];
		},

		/**
		 * Takes the new text and says what changed: `removed` lines at `at` gave way to `added` new ones, and
		 * `changed` lists every line, in the new numbering, whose colours have to be drawn again.
		 */
		update(text) {
			const next = text.split('\n');
			const oldCount = lines.length;
			const newCount = next.length;
			const shorter = Math.min(oldCount, newCount);
			let head = 0;
			while (head < shorter && lines[head] === next[head]) {
				head++;
			}

			let tail = 0;
			while (tail < shorter - head && lines[oldCount - 1 - tail] === next[newCount - 1 - tail]) {
				tail++;
			}

			const removed = oldCount - head - tail;
			const added = newCount - head - tail;
			if (removed === 0 && added === 0) {
				return { at: head, removed: 0, added: 0, changed: [] };
			}

			// The lines above the edit did not change, so neither did the state the first edited line starts in.
			let state = starts[head];
			const keptFrom = oldCount - tail;
			starts = starts.slice(0, head).concat(new Array(added).fill(null), starts.slice(keptFrom));
			tokens = tokens.slice(0, head).concat(new Array(added).fill(null), tokens.slice(keptFrom));
			lines = next;

			const changed = [];
			let index = head;
			for (; index < newCount; index++) {
				if (index >= head + added && starts[index] === state) {
					break;
				}

				starts[index] = state;
				const result = tokenizeLine(language, lines[index], state);
				tokens[index] = result.tokens;
				state = result.state;
				changed.push(index);
			}

			if (index === newCount) {
				starts[newCount] = state;
			}

			return { at: head, removed, added, changed };
		},
	};
}
