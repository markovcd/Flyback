# Honest tests

## Green means the code works, not that the test was moved

A failing test is fixed by fixing the code it caught. It is never fixed by moving the test out of the way:

- weakening an assertion: a wider tolerance on `ShouldBe`, a bit-for-bit comparison turned into a close one, asserting less of the result;
- deleting a failing test, or adding an `Assert.SkipWhen` for anything but a machine that lacks what the test needs;
- adding the failing module, opcode or preset to a theory's list of named exceptions;
- moving a `.received` over its `.verified` without reading the diff and agreeing with it;
- special-casing a test's input in `src/`: a branch on a test-only value, a hard-coded answer, a check for whether a test is running;
- catching and swallowing an error, or returning a default, so the symptom goes away while its cause stays;
- replacing the real thing under test with a double until nothing real is left to fail.

**Why:** a test is the one statement of a rule that a machine checks. Editing it to agree with broken code turns a caught bug into a hidden one, and the gate stops meaning anything.

**How to apply:**

- Fix the cause. If the cause is out of reach or out of scope, leave the test red and say so; a red test with an honest report beats a green one with a lie in it.
- If the test itself is wrong (it asserts the old behavior of something the task deliberately changes, or it never held), say which test, why, and what it should assert instead, then change it in the open: in the reply and in the commit message, never quietly alongside the fix.
- A tolerance, a deadline or a theory's exception list changes only with a comment beside it giving a reason that holds on its own. "To make it pass" is not one.
- A test that fails some runs and not others is a bug in the test or the code. Find which; do not rerun it until it passes.
- The IL, GLSL and JavaScript backends agree with the interpreter because the tests say so (ADR-0035). When one of them differs, the backend is wrong, not the comparison.
