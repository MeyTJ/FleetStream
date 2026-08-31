# FleetStream.UnitTests

Pure unit tests for the BFF API. No Docker, no Testcontainers, no real Redis or
Kafka. The whole suite should run in under 30 seconds on a laptop.

## Run

```bash
# from the solution root
cd BffApi
dotnet test tests/FleetStream.UnitTests

# with coverage
dotnet test tests/FleetStream.UnitTests \
    /p:CollectCoverage=true \
    /p:CoverletOutput=../coverage/ \
    /p:CoverletOutputFormat=lcopen
```

## Layout

| Folder | Scope |
| --- | --- |
| `Core/` | `BaseEntity`, `SoftDeletableEntity`, `ValueObject<T>` |
| `Application/Results/` | `Result<T>` / `Result` |
| `Application/Features/` | Validators + handlers (`GetFleetSummaryQueryHandler`, `GetTruckStatesQueryHandler`) |
| `Application/Decorators/` | `CommandValidationDecorator`, `QueryValidationDecorator` |
| `Infrastructure/` | `InMemoryTruckRepository` (the only infra class that is unit-testable without containers) |
| `Presentation/Auth/` | `DevTokenIssuer` |

## Stack

- **xUnit** 2.9 — test runner
- **NSubstitute** 5.x — mocking
- **FluentAssertions** 6.x — readable assertions
- **coverlet.collector** — coverage

## Conventions

- One test class per production class; one `[Fact]` / `[Theory]` per behaviour.
- Test names follow `Method_Scenario_Expectation`.
- Time is always injected through `TimeProvider` — never `DateTime.UtcNow`.
- No `Thread.Sleep`. Use `Task.Delay` only when asserting async ordering.