// Triggers: the patterns the settings hold, compiled once and run on one line of terminal output at a time. terminal.js
// runs them on what a session prints, and the settings page tests a sample line through testTriggers, so the page refuses
// exactly what a terminal would skip.
//
// JavaScript has no timeout for a regular expression, and these run on whatever a server sends. What keeps a bad pattern
// from freezing the page, in order:
// - a repeated group that itself repeats or holds alternatives, such as (a+)+, (\w+\s?)* or (a|aa)+, is refused before it
//   ever runs: that is the shape behind exponential backtracking, where a line of thirty characters can take hours;
// - so is a third unbounded repeat of something that matches nearly anything (.*, \S+, [^x]*): each one multiplies the
//   work on a line that almost matches by the length of the line, and three of them on a long line take minutes;
// - a pattern only ever sees the first MaxLineLength characters of a line, which keeps what the two checks above let
//   through (.*a.*b on a line that never matches) to a fraction of a second, and a line gives at most MaxMatches matches;
// - a rule that still takes longer than SlowMatchMs on one line is switched off in that terminal, which says so.
// The settings cap the number of rules and the length of a pattern on top of that. What none of this catches is a
// pattern that is slow some other way: it can hold the page up once, for one line, before it is switched off.

/** Characters of one line a pattern runs on. A log line rarely says anything past this that its start did not. */
export const MaxLineLength = 512;

/** Time one rule may take on one line before it is switched off. */
export const SlowMatchMs = 50;

const MaxMatches = 32;

// Unbounded repeats of something that matches nearly anything, allowed in one pattern.
const MaxWideRepeats = 2;

const NestedRepetition =
	'A repeated group that repeats inside, such as (a+)+ or (a|b)*, can take forever on some lines. '
	+ 'Use a character class such as [ab]+, or give the outer repeat a fixed count such as {3}.';

const TooManyWideRepeats =
	'More than two repeats that match nearly anything, such as .* or \\S+, can take minutes on one long line. '
	+ 'Keep to two, for example ERROR.*timeout.';

const TooSlow = 'This pattern took too long on that line, so a terminal would switch it off.';

/**
 * The end of the first `end` characters of `text`, moved back one where that would split a surrogate pair, so a cut
 * never leaves half a character behind.
 */
export function safeEnd(text, end) {
	if (end >= text.length) {
		return text.length;
	}

	const code = text.charCodeAt(end - 1);
	return code >= 0xd800 && code <= 0xdbff ? end - 1 : end;
}

/** `{ regex }` for a pattern a terminal can run, `{ error }` with the reason for one it cannot. */
export function checkPattern(pattern, ignoreCase) {
	if (typeof pattern !== 'string' || pattern.length === 0) {
		return { error: 'Enter a pattern.' };
	}

	let regex;
	try {
		regex = new RegExp(pattern, ignoreCase ? 'gi' : 'g');
	}
	catch (error) {
		return { error: syntaxMessage(error) };
	}

	// Checked after compiling, so the scan below only ever sees syntax JavaScript accepted.
	const risk = backtrackingRisk(pattern);
	return risk ? { error: risk } : { regex };
}

/**
 * The rules from .NET, compiled. A rule keeps its index in the list .NET sent, which is how a notice names it; one that
 * does not compile is left out, and the settings page is where the reason shows.
 */
export function compileTriggers(rules) {
	const compiled = [];
	(Array.isArray(rules) ? rules : []).forEach((rule, index) => {
		const { regex } = checkPattern(rule?.pattern, !!rule?.ignoreCase);
		if (regex) {
			compiled.push({
				index,
				regex,
				color: typeof rule.color === 'string' && rule.color.length > 0 ? rule.color : null,
				notify: !!rule.notify,
				lastNotice: -Infinity,
				off: false,
			});
		}
	});

	return compiled;
}

/** Where `regex` matches `text`, as `{ start, length }` offsets. Empty matches are skipped: they have nothing to color. */
export function matchRanges(regex, text) {
	const ranges = [];
	regex.lastIndex = 0;
	while (ranges.length < MaxMatches && regex.lastIndex <= text.length) {
		const match = regex.exec(text);
		if (!match) {
			break;
		}

		if (match[0].length === 0) {
			regex.lastIndex = match.index + 1;
			continue;
		}

		ranges.push({ start: match.index, length: match[0].length });
	}

	regex.lastIndex = 0;
	return ranges;
}

/**
 * The settings page's tester: each rule run on `sample` the way a terminal would run it on a line, as
 * `{ valid, error, matches }` in the order the rules came.
 */
export function testTriggers(rules, sample) {
	const text = typeof sample === 'string' ? sample.slice(0, safeEnd(sample, MaxLineLength)) : '';
	return (Array.isArray(rules) ? rules : []).map(rule => {
		const { regex, error } = checkPattern(rule?.pattern, !!rule?.ignoreCase);
		if (!regex) {
			return { valid: false, error, matches: [] };
		}

		const started = performance.now();
		const matches = matchRanges(regex, text);
		return performance.now() - started > SlowMatchMs
			? { valid: false, error: TooSlow, matches: [] }
			: { valid: true, error: null, matches };
	});
}

// The engine's message without its "Invalid regular expression: /.../flags: " opening, which repeats the pattern.
function syntaxMessage(error) {
	const message = String(error?.message ?? error ?? '').replace(/^Invalid regular expression: \/.*\/[a-z]*: /s, '');
	return message.length === 0 ? 'This is not a valid pattern.' : `${message[0].toUpperCase()}${message.slice(1)}.`;
}

/**
 * Why `source` could backtrack for far longer than a line deserves, or null. Two shapes are refused. A group with a
 * repeat of more than one (`*`, `+`, `{2,}`, `{1,5}`) that holds a repeat of its own or an alternation lets the engine
 * split the same text between the inner and the outer repeat in more ways than any line can afford; a fixed count over a
 * bounded group, such as (\d{1,3}\.){3}, stays allowed, since its work has a ceiling. And more than MaxWideRepeats
 * unbounded repeats of something that matches nearly anything multiply the work on a line that almost matches.
 */
function backtrackingRisk(source) {
	const stack = [frame()];
	let wideRepeats = 0;
	let i = 0;
	while (i < source.length) {
		const character = source[i];
		const current = stack[stack.length - 1];
		if (character === '(') {
			stack.push(frame());
			i = groupBodyStart(source, i + 1);
			continue;
		}

		if (character === ')') {
			const inner = stack.length > 1 ? stack.pop() : frame();
			const outer = stack[stack.length - 1];
			const repeat = readQuantifier(source, i + 1);
			if (repeat && repeat.max > 1) {
				if ((repeat.max === Infinity && (inner.varies || inner.alternation)) || inner.unbounded) {
					return NestedRepetition;
				}

				if (repeat.max === Infinity && inner.wide) {
					wideRepeats++;
				}
			}

			outer.varies ||= inner.varies || (repeat !== null && repeat.max > repeat.min);
			outer.unbounded ||= inner.unbounded || (repeat !== null && repeat.max === Infinity);
			outer.alternation ||= inner.alternation;
			outer.wide ||= inner.wide;
			i = repeat ? repeat.end : i + 1;
		}
		else if (character === '|') {
			current.alternation = true;
			i++;
		}
		else {
			const end = character === '\\' ? i + 2 : character === '[' ? classEnd(source, i) : i + 1;
			const wide = isWide(source.slice(i, end));
			const repeat = readQuantifier(source, end);
			current.wide ||= wide;
			if (repeat) {
				current.varies ||= repeat.max > repeat.min;
				current.unbounded ||= repeat.max === Infinity;
				if (wide && repeat.max === Infinity) {
					wideRepeats++;
				}
			}

			i = repeat ? repeat.end : end;
		}

		if (wideRepeats > MaxWideRepeats) {
			return TooManyWideRepeats;
		}
	}

	return null;
}

// An atom that matches nearly any character: the dot, \S, \W or \D, and a negated class or one that holds a class and
// its opposite, such as [\s\S].
function isWide(atom) {
	if (atom === '.' || atom === '\\S' || atom === '\\W' || atom === '\\D') {
		return true;
	}

	return atom.startsWith('[') && (atom.startsWith('[^') || /\\[SWD]/.test(atom));
}

function frame() {
	return { varies: false, unbounded: false, alternation: false, wide: false };
}

// Past the (?:, (?=, (?!, (?<=, (?<! or (?<name> that opens a group.
function groupBodyStart(source, i) {
	if (source[i] !== '?') {
		return i;
	}

	if (source[i + 1] === '<' && source[i + 2] !== '=' && source[i + 2] !== '!') {
		const close = source.indexOf('>', i);
		return close < 0 ? source.length : close + 1;
	}

	return i + (source[i + 1] === '<' ? 3 : 2);
}

// A character class ends at the first ']' that is not escaped; without the u flag even a ']' right after '[' closes it.
function classEnd(source, i) {
	for (let j = i + 1; j < source.length; j++) {
		if (source[j] === '\\') {
			j++;
		}
		else if (source[j] === ']') {
			return j + 1;
		}
	}

	return source.length;
}

// *, +, ?, {n}, {n,} or {n,m}, lazy or not, as { min, max, end }. A '{' that does not form a count is a plain character.
function readQuantifier(source, i) {
	let repeat = null;
	switch (source[i]) {
		case '*':
			repeat = { min: 0, max: Infinity, end: i + 1 };
			break;
		case '+':
			repeat = { min: 1, max: Infinity, end: i + 1 };
			break;
		case '?':
			repeat = { min: 0, max: 1, end: i + 1 };
			break;
		case '{': {
			const count = /^\{(\d+)(,(\d*))?\}/.exec(source.slice(i, i + 24));
			if (count) {
				const min = Number(count[1]);
				const max = count[2] === undefined ? min : count[3] === '' ? Infinity : Number(count[3]);
				repeat = { min, max, end: i + count[0].length };
			}

			break;
		}
	}

	if (repeat && source[repeat.end] === '?') {
		repeat.end++;
	}

	return repeat;
}
