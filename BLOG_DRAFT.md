# Stop Validating Email With Regex. It's Not Just Wrong — It's a Security Bug Waiting to Happen.

You've had this conversation before:

> "We need to validate email addresses."
> "Just use a regex."
> "Here's one: `/^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/`"

Everyone nods. Ticket closed. Nobody's right.

## Email addresses are not a regular language

RFC 5321 and RFC 5322 allow quoting, escaping, comments, and context-sensitive rules that a regular expression cannot express without approximating — and an approximation of a grammar is, by definition, sometimes wrong. Regex either rejects valid addresses (quoted locals, plus-addressing, provider-specific dot handling) or silently accepts addresses that can never receive mail. There is no regex that does both correctly, because the problem isn't pattern matching — it's parsing a grammar, checking policy, and confirming a mail server exists and accepts mail. Three different jobs, and regex only pretends to do the first one.

That's the argument everyone's heard. Here's the one most people haven't.

## The part nobody mentions: regex email validation is a live security bug class

"Sloppy" undersells it. Poorly constructed email regexes are a recurring source of **ReDoS — Regular Expression Denial of Service**. Certain shapes (nested quantifiers, ambiguous repetition over the local-part) send backtracking regex engines into exponential-time evaluation on a single crafted string. Feed one carefully chosen "email address" into the wrong regex and you don't get a rejected signup — you get a hung worker thread, and enough of those take down a service. This isn't hypothetical or rare; it's one of the most common ReDoS patterns catalogued across vulnerability databases, precisely because email validation regexes are copy-pasted into almost every codebase that's ever existed.

Character-by-character, non-backtracking validation is structurally immune to this. Not "less likely" — architecturally incapable of it, because there's no backtracking engine in the loop at all.

## Even the good alternatives get one thing wrong

To be fair, not everyone reaches for raw regex. Some of the better tools out there — [python-email-validator](https://github.com/JoshData/python-email-validator) is the standout — do real RFC-aware parsing and optional MX record checks. That's real engineering, and it's better than 95% of what ships in production.

But even it does this, by its own documentation:

> "If there is no MX record, a fallback A/AAAA-record is permitted."

That's RFC 974 behavior — deprecated, operationally unsafe, and the exact reason "validated" email addresses end up unable to ever receive mail. A domain having an A record means a web server exists there. It says nothing about whether anything on the other end of port 25 will accept a message. **No MX record means no mail delivery, full stop** — and quietly falling back to "well, *something* resolved" is how you get silent password-reset failures and "validated" leads that were never real.

We looked hard for a library — in .NET, Python, Go, Node, PHP, Ruby — that combines strict character-level RFC parsing, zero regex, *and* mandatory MX-only validation with no fallback. We didn't find one. Most tools get one or two of those three. Nothing we found gets all three.

## Why this shouldn't be a library at all

Here's the part that's easy to miss: even if you write the parsing correctly, you still have a DNS lookup to do — and DNS lookups are I/O, with all the problems I/O brings. Timeouts. Caching. Retry behavior. Async correctness in whatever runtime you're in. Every library that "does MX checking" is quietly asking every application that imports it to solve that problem itself, again, from scratch, in whatever language it happens to be written in.

That's backwards. Deliverability checking is an infrastructure problem, not an application problem — it belongs behind one API, solved once, correctly, and reused by every service that needs it regardless of language. That's why this isn't a NuGet package: it's a standalone microservice you drop behind your firewall and call over HTTP, the same way you'd call any other piece of shared infrastructure.

## What we built

- **Zero regex.** Character-by-character validation against RFC 5321/5322, not pattern matching.
- **Mandatory MX checking, no A-record fallback.** No mail server, no "valid."
- **223 tests, each one annotated with the specific RFC section it enforces** — not just "assert true."
- **A REST API**, not a library, because DNS resolution is infrastructure, not application logic.
- **MIT licensed.** Self-host it behind your own firewall.

It's not trying to be everything — no SMTP probing (unreliable, gets your IPs blocked), no disposable-domain detection, no internationalized-address support yet. It does one job: tell you, correctly, whether an email address is structurally valid and can theoretically receive mail. If your system needs more than that, layer it on top. If your system needs *less* than that — a regex — you already know how that story ends.

**Repo:** https://github.com/pengdows/email-validation-service

If you've got the regex-vs-real-validation argument on a loop at your job, send them here.
