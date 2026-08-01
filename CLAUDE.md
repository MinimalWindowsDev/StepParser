# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**StepParser** is a C# CLI that tokenizes, lexes, and parses ISO 10303-21 (STEP Part 21) clear-text
files, with an AP242 (ISO 10303-242, Managed Model-Based 3D Engineering) entity catalog for
classifying PMI/MBD content. It is consumed as a git submodule by the parent `MBDInspector`
solution.

## Build, Test, Run

Every command needs these two environment variables first — the repo keeps its NuGet packages in a
repo-local `.dotnet_home/` (gitignored) rather than the user profile cache:

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet_home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
```

```powershell
dotnet build StepParser.slnx --ignore-failed-sources
dotnet test StepParser.slnx
dotnet test StepParser.slnx --filter "FullyQualifiedName~ParsesMinimalSample"   # one test
dotnet run --project src/StepParser/StepParser.csproj -c Debug --no-build -- --phase parse .\sample.stp
```

**Coverage and mutation testing.** Coverage alone is gameable — an assertion-free test scores 100%.
Mutation testing is the check that the tests actually constrain behaviour, so treat the Stryker
score as the real number and coverage as a floor:

```powershell
dotnet test StepParser.slnx --collect:"XPlat Code Coverage"
dotnet tool restore
dotnet stryker
```

**Corpus sweep** — bulk-parses `.stp`/`.step` paths read from **stdin**, writing a CSV plus an
exit-code histogram. It re-enters the ordinary single-file pipeline per file, so a sweep exercises
exactly the code path a single invocation does:

```powershell
$files = (@(es.exe *.stp) + @(es.exe *.step)) | Sort-Object -Unique
$files | dotnet run --project src/StepParser/StepParser.csproj -c Debug --no-build -- --sweep .\step_sweep_results.csv
```

## Architecture

### The parser never throws — it accumulates diagnostics

This is the single most important invariant before editing anything under `Parser/` or `Lexer/`.
`StepFileParser.Expect()` does **not** throw on a token mismatch: it appends a `ParseDiagnostic`
and returns the offending token **without advancing the position**. Callers therefore continue with
a token they did not ask for. Malformed input yields a populated `StepFile` plus diagnostics, never
an exception — which is what lets sweep mode grind a whole corpus without aborting.

A corollary: an unconsumed token can loop if you add a `while` that depends on `Expect` advancing.
The existing loops all guard on `TokenKind.EndOfFile` or `EndSec` for this reason, and `Advance()`
deliberately clamps at the last token rather than running off the end.

### One mutable diagnostics list threads through all three phases

`StepParserCli.Run` creates a single `List<ParseDiagnostic>` and passes it by reference into
`Tokenizer.TokenizeFile` → `Lexer.Lex` → `new StepFileParser(tokens, diagnostics)`. Each phase
appends to it. Diagnostics from an earlier phase are therefore visible in a later phase's output,
and `--phase tokens` still reports tokenizer-stage errors. Nothing clears or partitions this list.

### Phases are early-exit, not separate entry points

`--phase tokens|lex|parse` runs the same linear function and returns early with a different payload
type (`TokenizationResult` / `LexingResult` / `ParseResult`), all funnelled through the untyped
`WriteResult(..., object payload, ...)`.

### JSON output goes through a hand-written projection, not reflection

`JsonFormatter` serializes `ParseResult` via `ParseResultJsonProjection.Create` and
`ParameterJsonProjection.Create` (both in `Parser/ParseResult.cs`), which build anonymous objects
with explicit camelCase keys and a `kind` discriminator per parameter variant. **Adding a property
to `ParseResult` or a new `Parameter` subtype will not appear in JSON until you also update the
projection**, and an unhandled `Parameter` case falls through to `{ kind = "unknown" }`, silently
losing the value. `InvariantTests.EveryParameterVariantHasItsOwnJsonDiscriminator` walks the type
hierarchy by reflection and fails if a variant is ever added without extending the projection —
keep that test working rather than routing around it.

Non-`ParseResult` payloads (the tokens/lex phases) bypass the projection and are reflected directly.

### Logging is compile-time woven, not a runtime call

`Logging/LogAttribute.cs` derives from `MethodBoundaryAspect.Fody.Attributes.OnMethodBoundaryAspect`.
Fody rewrites the IL at build time so every `[Log]`-decorated method gets entry/exit/exception hooks
forwarding to Serilog through `StepLogger`. All hooks return immediately when `StepLogger.IsEnabled`
is false, so cost is near-zero unless `--log` is passed. `[Log]` marks the CLI entry point, both
tokenizer entry points, `Lexer.Lex`, and the top-level parse methods.

Consequences worth knowing:

- Weaving happens in the `StepParser` assembly only. A `[Log]` attribute applied inside the test
  project is inert, so aspect behaviour is tested by driving the real decorated methods.
- `StepParserCli.Run` calls `StepLogger.Configure` from its own options, so a logger configured by
  a caller beforehand is overwritten. Switch logging on with `--log`/`--log-file`, not externally.
- Serilog's file sink holds an exclusive handle until disposed. `StepLogger.Configure` disposes the
  previous logger, and `Program.Main` calls `StepLogger.CloseAndFlush()` in a `finally` — without
  that the last buffered lines are lost on exit.

### The AP242 semantic model runs on every parse

`Parser/Ap242SemanticModel.cs` is the largest file in the repo (~1080 lines): thirteen commented
"Module" blocks of `HashSet<string>` entity-name catalogs (geometry, topology, representation,
product structure, assembly, units, PMI dimensions/tolerances/datums/shape aspects/presentation,
materials, kinematics) plus `Ap242SemanticModelBuilder.Build(StepFile)`.

`ParseResult.FromStepFile` calls `Build`, so the summary reaches both the JSON `semantics` section
and the text `AP242: … | MBD: …` line. (It was unreachable dead code until it was wired in; the
`DocumentationContractTests` guard against it silently becoming dead again.)

Two properties of `Build` that look like bugs and are not accidental:

- `geometry` and `representation` counters use substring matching on the entity name
  (`Contains("CURVE")`, `Contains("CONTEXT")`, …), not the curated catalogs. Only `pmi` and `gdt`
  are catalog-driven.
- `HasModelBasedDefinition` requires *both* an AP242 schema and PMI/GD&T content.

**Known defect, deliberately pinned by a test:** `BuildDimension` scans every `MEASURE_WITH_UNIT`
in the file rather than following references from the dimension, so a file with two dimensions gives
both the same values. `Ap242SemanticModelTests.KnownLimitation_MultipleDimensionsShareGloballyScannedMeasures`
documents this. When it is fixed that test should fail and be rewritten to assert per-dimension
resolution.

## Exit Codes

The CLI's machine-readable contract; `--sweep` groups its histogram on these.

| Code | Meaning |
|---|---|
| 0 | clean |
| 1 | input file not found |
| 2 | errors present, unhandled exception, or any warning under `--strict` |
| 3 | warnings only |
| 4 | bad arguments |

`--strict` also writes a `strict: N warning(s) promoted to error(s) — exit 2` line to stderr so the
promotion is observable without reading the exit code.

## Testing

`dotnet test`, xUnit, in `tests/StepParser.Tests`. Parallelisation is disabled assembly-wide
(`GlobalUsings.cs`) because `StepLogger` is process-wide static state.

Beyond conventional per-unit tests, three suites exist specifically to catch failures that ordinary
tests cannot see. Understand what each is for before modifying it:

- **`InvariantTests`** — properties rather than examples: the exit code cannot contradict the
  diagnostics, `stats.entityCount` cannot disagree with the emitted entity list, text and JSON must
  report the same diagnostic count, output must be byte-identical across runs, and garbage input
  must never be reported as clean. These fail when a change makes the tool *lie about its own
  result*, which example-based tests happily miss.
- **`DocumentationContractTests`** — parse `README.md` and this file and check the claims against
  the running program: every documented flag is accepted, every advertised flag is documented, every
  documented exit code is reachable, and advertised features actually appear in the output. Added
  after two real drifts: an AP242 layer nothing called, and tail-remark comment support the
  tokenizer never had.
- **`GoldenCorpusTests`** — checked-in `.stp` fixtures with checked-in expected JSON. A behaviour
  change must be re-blessed explicitly rather than silently absorbed.

If a documentation-contract test fails, the default fix is to correct the **code or the claim**,
not to loosen the assertion.

### Measured state

355 tests. **100.00% line coverage, 98.47% branch, 93.83% mutation score** (Stryker, 1372 killed /
72 timeout / 52 survived).

Branch coverage is not 100% because six guards are provably unreachable. They are listed here so
nobody spends an afternoon on them — and so nobody raises the number by deleting a guard:

| Location | Guard | Why unreachable |
|---|---|---|
| `StepFileParser.cs` `Current` | `_position < _tokens.Count ? … : _tokens[^1]` | `Advance()` clamps at `Count - 1` |
| `Tokenizer.cs` `Peek` | `index >= 0 && …` | every caller passes `index + 1` with `index >= 0` |
| `LogAttribute.cs` ×3 | `args.Method.DeclaringType?.Name ?? "?"` | a woven method always has a declaring type |
| `LogAttribute.cs` `RenderValue` | `value.ToString() ?? "?"` | no decorated method's argument type returns null from `ToString` |
| `SweepRunner.cs` `FirstLine` | `reader.ReadLine() ?? string.Empty` | empty and whitespace input return earlier |

The surviving mutants are almost entirely **log-message** mutations (deleting a `StepLogger.Debug`
call or blanking its template changes no observable behaviour) plus the equivalent mutants above.
One is genuinely unkillable without a flaky assertion: `SweepRunner`'s `TOP_SLOW` ordering depends
on measured wall-clock durations.

The AP242 entity-name catalogs are excluded from mutation with a `// Stryker disable all` marker —
they are reference data, and mutating ~600 string literals would swamp the score with mutants no
sane test could kill. `Build()` and its helpers below the `// Stryker restore all` marker *are*
mutated.

## Dependencies & Build Configuration

- **Target `net10.0`**, `Nullable=enable`, `ImplicitUsings=disable` (a short `GlobalUsings.cs` per
  project supplies `System`, `System.Collections.Generic`, `System.IO`, `System.Linq`),
  `LangVersion=latest`. Solution is the XML `.slnx` format.
- `src/StepParser` references Fody (build-only, `PrivateAssets=all`), MethodBoundaryAspect.Fody
  (weaver *and* runtime base types), Serilog, Serilog.Sinks.Console, Serilog.Sinks.File.
  `--ignore-failed-sources` tolerates a dead feed, not an empty package cache.
- `tests/StepParser.Tests` references xUnit, Microsoft.NET.Test.Sdk, xunit.runner.visualstudio and
  coverlet.collector.
- `src/StepParser` grants `InternalsVisibleTo("StepParser.Tests")` so `SweepRunner` helpers and the
  JSON projections can be tested without widening the public API.

## Parser Behaviour Worth Knowing

- **Comments:** `/* … */` blocks only. Tail remarks are not implemented — a stray `/` produces an
  `Unrecognized character` warning. Do not document otherwise; a test enforces this.
- **Complex-entity recovery.** ISO 10303-21 §11.3.3 requires `#id=(TYPE1(...)TYPE2(...));`. Files
  that omit the outer parens are recovered into an `EntityInstance` with `Components`, emitting a
  `Warning` whose message cites `11.3.3` — asserted verbatim by a regression test.
- **`EntityInstance` is either/or.** Simple ⇒ `Name` + `Parameters`, `Components == null`. Complex ⇒
  `Name == null`, empty `Parameters`, populated `Components`. `ParseResult.FromStepFile` counts
  entity-type frequencies per *component* for complex instances.
- **Duplicate `#id` silently overwrites** — `ParseDataSection` assigns into a `Dictionary<int, …>`.
- **`ANCHOR` / `REFERENCE` / `SIGNATURE` sections are skipped wholesale**, not parsed.
- **Edition detection** reads only the first character of `IMPLEMENTATION_LEVEL`.
- **Unicode `\X2\` / `\X4\` escapes** are decoded in the lexer, as is `''` → `'`.
