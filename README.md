# Roslyn Architecture Guidelines Analyzer

This repository contains a C# Roslyn analyzer for the architecture and coding
guidelines described by its diagnostic IDs.

| ID | Guideline |
| --- | --- |
| RAG001 | Avoid `global::`-qualified names |
| RAG002 | Avoid local functions |
| RAG003 | Avoid `ConfigureAwait(false)` |
| RAG004 | Keep one declared type per file |
| RAG005 | Remove unused using directives and sort the remaining directives |
| RAG006 | Do not leave catch blocks empty |
| RAG007 | Use centralized logging instead of `System.Diagnostics` or `Console.WriteLine` |
| RAG008 | Inline methods whose only action is returning a new object |
| RAG009 | Do not use optional constructor parameters or fallback object creation |
| RAG010 | Keep services focused on one distinct return-type contract |
| RAG011 | Do not inject exposure-layer types into services |
| RAG012 | Keep exposure types free of data except injected service dependencies |
| RAG013 | Keep service constructors limited to dependency assignment |
| RAG014 | Keep extension methods as direct one-call pass-throughs |
| RAG015 | Keep business logic in services or brokers; other methods must be direct one-call pass-throughs |
| RAG016 | Keep exposures limited to direct calls to injected services |
| RAG017 | Keep service dependencies within the Standard's layer hierarchy |
| RAG018 | Require one Foundation dependency for Processing; two or three service dependencies for orchestration-like services |
| RAG019 | Do not mix Foundation and Processing dependencies in an Orchestration service |
| RAG020 | Limit Foundation services to one entity broker; other service levels may depend only on utility brokers |
| RAG021 | Match each Processing service to its corresponding Foundation service |
| RAG022 | Keep Aggregation service dependencies within one service variation |
| RAG023 | Keep service methods instance-based to support unit testing |
| RAG024 | Place files in Exposures, Models, Services, or Brokers folders (except `Program.cs`) |
| RAG025 | Keep files under `Models` as data carriers with auto-properties only |
| RAG026 | Place enums in any folder under `Models` |

Build the analyzer with `dotnet build Analyzer/Analyzer.csproj` and run its
tests with `dotnet test Analyzer.Tests/Analyzer.Tests.csproj`. The diagnostics
are warnings and can be configured in `.editorconfig` by their `RAG` IDs.

The analyzer package also includes IDE code fixes for RAG001, RAG003, RAG005,
and RAG006: remove `global::`, remove `ConfigureAwait(false)`, sort or remove
using directives, and rethrow from an empty catch block. The RAG006 fix changes
behavior by rethrowing an exception that was previously swallowed. Other
diagnostics describe architectural changes without a unique safe rewrite, so
they remain manual fixes.

The project also provides a [The Standard development agent skill](./.github/skills/the-standard-development/SKILL.md)
for planning, implementing, reviewing, and validating Standard-oriented C#
changes. It is included in the NuGet package at
`contentFiles/any/any/.github/skills/the-standard-development/SKILL.md`; copy
that folder into a consuming repository's `.github/skills` directory to make
the skill discoverable by compatible coding agents.

Create a NuGet package with `dotnet pack Analyzer/Analyzer.csproj`. The
`Publish NuGet package` GitHub Actions workflow tests, packs, and publishes
the package to GitHub Packages whenever a GitHub Release is published. The
release tag supplies the package version, with an optional leading `v`
removed (for example, `v1.2.3` publishes version `1.2.3`). The workflow can
also be run manually from the Actions tab by supplying a release tag.

The workflow uses the repository's `GITHUB_TOKEN` to publish. Add a
`GH_PACKAGES_TOKEN` Actions secret containing a classic personal access token
with `write:packages` scope; the workflow uses it through GitHub CLI to set the
package visibility to **Public** after publishing. The token owner must have
permission to manage the package (and, for an organization, any required SSO
authorization). Add the resulting `RoslynArchitectureGuidelines` package to
a consuming project to run the analyzer during builds.

Service, broker, and exposure rules identify types by convention: services are
in a `Services` namespace or have a name ending in `Service`; brokers are in a
`Brokers` namespace or have a name ending in `Broker`; exposure types are in an
`Exposures` namespace or have a name ending in `Exposure`. Direct pass-through
methods contain a single invocation and no other statements or computation.

The service hierarchy analyzer classifies service levels by suffix:
`FooService`, `FooProcessingService`, `FooOrchestrationService`,
`FooCoordinationService`, `FooManagementService`,
`FooUberManagementService`, and `FooAggregationService`. Utility broker
dependencies are identified by a `UtilityBrokers` namespace. The hierarchy
checks follow [The Standard's service guidance](https://github.com/hassanhabib/The-Standard/tree/master/2.%20Services),
including its [dependency limits and layer balance](https://github.com/hassanhabib/The-Standard/blob/master/2.%20Services/2.%20Services.md#2002-overall-rules).
# callum-analyzer
