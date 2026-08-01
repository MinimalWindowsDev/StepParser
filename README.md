# StepParser

`StepParser` is a C# CLI for ISO 10303-21 clear-text STEP files. It tokenizes, lexes, and parses STEP exchange files, with an AP242 (ISO 10303-242) Managed Model-Based 3D Engineering entity catalog used to classify PMI/MBD content.

## Features

- Tokenization with block-comment (`/* … */`) stripping and precise source coordinates
- Lexer covering STEP structural keywords, string/enum/binary literals, entity references, and typed parameters including Unicode escape sequences (`\X2\`, `\X4\`)
- Recursive-descent parser for `HEADER` and `DATA` sections that reports diagnostics instead of throwing
- Recovery from non-conformant complex entity instances (missing outer parentheses per ISO 10303-21 §11.3.3) — emits a `WARNING` diagnostic and continues rather than failing
- AP242 semantic model: ~600 entity types across geometry, topology, representations, product structure, assemblies, units/measurement, PMI dimensions, GD&T, datums, shape aspects, presentations, materials, and kinematics — surfaced as the `semantics` section of the output
- JSON and text output modes
- AOP verbose debug logging via Fody/Serilog (`--log` / `--log-file`)
- Corpus sweep mode (`--sweep`) for bulk processing of `.stp`/`.step` files

## Build

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet_home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
dotnet build StepParser.slnx --ignore-failed-sources
```

## Test

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet_home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
dotnet test StepParser.slnx
```

Run a single test with `dotnet test --filter "FullyQualifiedName~ParsesMinimalSample"`.

Coverage and mutation testing:

```powershell
dotnet test StepParser.slnx --collect:"XPlat Code Coverage"
dotnet stryker                        # requires: dotnet tool restore
```

## Run

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet_home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
dotnet run --project src/StepParser/StepParser.csproj -c Debug --no-build -- sample.stp
```

## CLI Options

```
Usage: StepParser [OPTIONS] <input-file>

Options:
  -o, --output <file|auto>   Write output to a file. Use 'auto' for <input>.json beside the input.
  -f, --format <fmt>         Output format: json (default), text
  --phase <phase>            Stop after phase: tokens, lex, parse (default: parse)
  --strict                   Treat warnings as errors (exit 2; prints annotation to stderr)
  --no-color                 Disable ANSI colour in text output
  -v, --verbose              Print progress to stderr
  --log                      Enable AOP verbose debug logging
  --log-file <path>          Log file path (default: <input>.log beside the input file)
  -h, --help                 Show help
  --version                  Show version

Corpus sweep:
  --sweep <csv>              Parse every path read from stdin; write per-file results to <csv>
```

Exit codes: `0` clean, `1` input file not found, `2` errors (or warnings under `--strict`), `3` warnings only, `4` bad arguments.

## Text Output

Text format (`--format text`) includes:

- Source path, detected schema, STEP edition
- `Errors: N | Warnings: N` counts (or `Diagnostics: 0` when clean)
- All diagnostic messages with severity, line:column, and message
- An `AP242: … | MBD: … | PMI: N | GD&T: N` summary line, plus any detected dimensions and datums
- Entity-type frequency breakdown (top 20 by count)

## Sweep Local STEP Files

```powershell
$files = (@(es.exe *.stp) + @(es.exe *.step)) | Sort-Object -Unique
$files | dotnet run --project src/StepParser/StepParser.csproj -c Debug --no-build -- --sweep .\step_sweep_results.csv
```

## License

GPL-3.0 — see [LICENSE](LICENSE).
