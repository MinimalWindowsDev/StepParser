# StepParser

`StepParser` is a C# CLI for ISO 10303-21 clear-text STEP files. The current scope is an MVP tokenizer, lexer, parser, plus an initial AP242-oriented semantic layer for extracting header/schema information and identifying PMI/MBD-related entities.

## Current Scope

- Tokenization with comment stripping and source coordinates
- Lexing for STEP structural keywords, literals, references, enums, and typed parameters
- Recursive-descent parsing for `HEADER` and `DATA`
- JSON and text output modes
- Corpus sweep support for `.stp` and `.step` files found with `es.exe`
- Initial AP242 semantic summaries and PMI/MBD entity classification

## Environment

This workspace builds offline against the SDK installed on the machine. The runnable projects therefore target `net10.0`.

## Build

```powershell
$env:DOTNET_CLI_HOME="$PWD\\.dotnet_home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
dotnet build StepParser.slnx --ignore-failed-sources
```

## Test

The test project is a zero-dependency console runner so it can execute offline without restoring external test packages.

```powershell
$env:DOTNET_CLI_HOME="$PWD\\.dotnet_home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
dotnet run --project tests/StepParser.Tests/StepParser.Tests.csproj -c Debug --no-build
```

## Run

```powershell
$env:DOTNET_CLI_HOME="$PWD\\.dotnet_home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
dotnet run --project src/StepParser/StepParser.csproj -c Debug --no-build -- --phase parse .\\sample.stp
```

## CLI Options

- `--phase tokens|lex|parse`
- `--format json|text`
- `--output <path|auto>`
- `--strict`
- `--no-color`
- `--verbose`

## Sweep Local STEP Files

```powershell
$files = (@(es.exe *.stp) + @(es.exe *.step)) | Sort-Object -Unique
$files | dotnet run --project tests/StepParser.Tests/StepParser.Tests.csproj -c Debug --no-build -- --sweep .\\step_sweep_results.csv
```

## Status

- Local parser tests: passing
- Machine sweep: 834 files processed, 808 accepted, 26 rejected with diagnostics
- Remaining rejected files include malformed/non-Part-21 inputs and a few AP242/PMI-oriented cases that need deeper semantic handling
