# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**StepParser** is a C# CLI tool for parsing ISO 10303-21 (STEP) files—a standard format for exchanging 3D product design data. The current scope covers an MVP tokenizer, lexer, parser, and AP242-oriented semantic layer for extracting header/schema information and identifying PMI/MBD-related entities.

The parser is intentionally zero-dependency and offline-first, designed to work in offline environments without NuGet restore.

## Build & Run Commands

All commands should set `DOTNET_CLI_HOME` and `DOTNET_SKIP_FIRST_TIME_EXPERIENCE` environment variables for offline compatibility.

### Build
```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet_home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
dotnet build StepParser.slnx --ignore-failed-sources
```

### Run CLI
```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet_home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
dotnet run --project src/StepParser/StepParser.csproj -c Debug --no-build -- --phase parse .\sample.stp
```

**CLI Options:**
- `--phase tokens|lex|parse` — execution phase (default: parse)
- `--format json|text` — output format (default: json)
- `--output <path|auto>` — output file path or auto-generate
- `--strict` — enable strict validation
- `--no-color` — disable terminal colors
- `--verbose` — verbose output
- First positional argument is the input `.stp` or `.step` file path

### Run Tests
```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet_home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
dotnet run --project tests/StepParser.Tests/StepParser.Tests.csproj -c Debug --no-build
```

Tests use a custom zero-dependency test runner (no xUnit or NUnit). Add new tests to the `tests` list in `tests/StepParser.Tests/Program.cs`.

### Bulk File Processing (Sweep)
```powershell
$files = (@(es.exe *.stp) + @(es.exe *.step)) | Sort-Object -Unique
$files | dotnet run --project tests/StepParser.Tests/StepParser.Tests.csproj -c Debug --no-build -- --sweep .\step_sweep_results.csv
```

This processes all STEP files and outputs a CSV with diagnostics for batch analysis.

## Architecture

### Execution Pipeline

The parser follows a three-phase pipeline, each producing structured output:

1. **Tokenization** (`Lexer/Tokenizer.cs`) — Raw character-by-character tokenization with:
   - Comment stripping (ISO 10303-21 `/*` and `//`)
   - Source coordinate tracking (line, column)
   - Escape sequence handling for strings

2. **Lexing** (`Lexer/Lexer.cs`) — Converts raw tokens into semantic tokens with:
   - STEP keyword recognition (HEADER, DATA, ENDSEC, etc.)
   - Literal classification (strings, integers, floats, enums, references)
   - Typed parameter parsing

3. **Parsing** (`Parser/StepFileParser.cs`) — Recursive-descent parser that:
   - Validates ISO-10303-21 file structure (ISO_OPEN → HEADER → DATA → ISO_CLOSE)
   - Extracts HEADER entities (FILE_DESCRIPTION, FILE_NAME, FILE_SCHEMA, etc.)
   - Parses DATA section entity instances and references
   - Collects diagnostics (errors/warnings) during parsing
   - Detects STEP edition from IMPLEMENTATION_LEVEL

### Key Data Structures

- **StepFile** — Root parse result containing header, entities, detected edition, and schema
- **HeaderSection** — Ordered list of HEADER entities (name + parameter list)
- **EntityInstance** — DATA section entity with ID, type, and parameters
- **Parameter** — Typed parameter (string, int, float, enum, reference, or nested list)
- **Token/RawToken** — Lexed token with kind, value, and source location
- **ParseDiagnostic** — Error/warning with severity, location, and message

### Output Formatters

- **JsonFormatter** — Structured JSON output of parse results (default)
- **TextFormatter** — Human-readable text summary for debugging

### Semantic Layer

**Ap242SemanticModel.cs** provides AP242-oriented analysis:
- Extracts and summarizes HEADER information (application, origin, schema)
- Classifies DATA entities as PMI/MBD-related
- Maps STEP entity names to semantic categories

## Project Structure

```
src/StepParser/
├── Cli/                    # CLI option parsing and orchestration
│   ├── CliOptions.cs
│   ├── ExecutionPhase.cs   # tokens | lex | parse
│   ├── OutputFormat.cs
│   └── StepParserCli.cs    # Main entry point
├── Lexer/                  # Tokenization and lexing
│   ├── Tokenizer.cs        # Raw character → RawToken
│   ├── Lexer.cs            # RawToken → Token (semantic)
│   ├── Token.cs
│   ├── RawToken.cs
│   └── TokenKind.cs
├── Parser/                 # Parsing and semantic analysis
│   ├── StepFileParser.cs   # Main parser logic
│   ├── StepFile.cs
│   ├── HeaderSection.cs
│   ├── EntityInstance.cs
│   ├── Parameter.cs
│   ├── ParseResult.cs
│   ├── PhaseResults.cs
│   └── Ap242SemanticModel.cs
├── Output/                 # Output formatting
│   ├── IOutputFormatter.cs
│   ├── JsonFormatter.cs
│   └── TextFormatter.cs
├── Diagnostics/            # Error/warning reporting
│   ├── ParseDiagnostic.cs
│   └── DiagnosticSeverity.cs
├── Program.cs              # Entry point
└── GlobalUsings.cs

tests/StepParser.Tests/
├── Program.cs              # Custom test runner
├── StepSample.cs           # Test data
└── GlobalUsings.cs
```

## Configuration & Build Details

- **Target Framework**: `net10.0` — requires .NET 10 SDK installed offline
- **Nullable Strict**: Enabled across all projects
- **Implicit Usings**: Disabled (explicit namespaces required)
- **Latest C# LangVersion**: Uses cutting-edge C# features
- **No External Dependencies**: Zero NuGet packages (offline-compatible)
- **Solution Format**: Modern `.slnx` (solution explorer format)

Environment variables required for offline builds:
- `DOTNET_CLI_HOME="$PWD\.dotnet_home"` — local NuGet cache
- `DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"` — skip telemetry and welcome

## Testing Notes

The test framework is custom-built (zero dependencies) and runs as a console app with explicit test methods. When adding tests:

1. Define the test method in `tests/StepParser.Tests/Program.cs`
2. Add it to the `tests` list in `Main()`
3. Exceptions are caught and reported as failures
4. Use the `--sweep` mode for corpus analysis across many STEP files

Current test status:
- Local parser tests: **passing**
- Machine sweep: **834 files processed**, 808 accepted, 26 rejected with diagnostics
- Remaining failures: Malformed/non-Part-21 inputs and some AP242/PMI edge cases

## Common Development Tasks

### Running a single test
Add a standalone test call:
```powershell
dotnet run --project tests/StepParser.Tests/StepParser.Tests.csproj -c Debug --no-build
```
Then modify the test list in `Program.cs` to run only the test you want.

### Analyzing parser failures
Use the `--verbose` flag on the CLI:
```powershell
dotnet run --project src/StepParser/StepParser.csproj -c Debug --no-build -- --verbose --phase parse .\failing_file.stp
```

### Checking output format
Run with both formatters to compare:
```powershell
dotnet run --project src/StepParser/StepParser.csproj -c Debug --no-build -- --format json .\sample.stp
dotnet run --project src/StepParser/StepParser.csproj -c Debug --no-build -- --format text .\sample.stp
```

### Debugging tokenizer issues
Use `--phase tokens` to see raw token stream, then `--phase lex` to see lexed tokens:
```powershell
dotnet run --project src/StepParser/StepParser.csproj -c Debug --no-build -- --phase tokens .\sample.stp
```
