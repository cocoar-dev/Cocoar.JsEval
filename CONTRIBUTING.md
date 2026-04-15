# Contributing

Thanks for your interest in contributing to Cocoar.JsEval!

## Getting started

- Fork the repository and create a feature branch.
- Ensure you have the .NET 10 SDK installed.
- Run the test suite locally before opening a PR.

## Coding guidelines

- Prefer small, focused PRs.
- Keep public APIs stable.
- Add or update tests for all behavior changes.
- Module names must be lowercase.

## Testing

```bash
# Run all tests
dotnet test src/Cocoar.JsEval.slnx --filter "FullyQualifiedName!~Fetch"

# Run benchmarks
cd src/Benchmarks/JsEval.Benchmarks && dotnet run -c Release -- --filter "*"
```

## Commit/PR

- Reference related issues in the PR description.
- Describe user-facing changes and migration notes if any.

## License

By contributing, you agree that your contributions will be licensed under the [Apache License 2.0](LICENSE.txt).
