### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RAG001 | Architecture | Warning | Avoid global-qualified names
RAG002 | Architecture | Warning | Avoid local functions
RAG003 | Architecture | Warning | Avoid ConfigureAwait(false)
RAG004 | Architecture | Warning | Use one type per file
RAG005 | Architecture | Warning | Remove unused or sort using directives
RAG006 | Architecture | Warning | Do not leave catch blocks empty
RAG007 | Architecture | Warning | Use centralized logging
RAG008 | Architecture | Warning | Inline object creation
RAG009 | Architecture | Warning | Require constructor dependencies
RAG010 | Architecture | Warning | Keep services focused on one contract
RAG011 | Architecture | Warning | Services must not depend on exposure types
RAG012 | Architecture | Warning | Keep exposure types free of data except injected services
RAG013 | Architecture | Warning | Keep service constructors declarative
RAG014 | Architecture | Warning | Keep extension methods as pass-throughs
RAG015 | Architecture | Warning | Keep business logic in services or brokers
RAG016 | Architecture | Warning | Keep exposures as service pass-throughs
RAG017 | Architecture | Warning | Follow the service hierarchy
RAG018 | Architecture | Warning | Respect service dependency limits
RAG019 | Architecture | Warning | Keep orchestration dependencies at one level
RAG020 | Architecture | Warning | Keep broker dependencies within service limits
RAG021 | Architecture | Warning | Match processing services to their foundation service
RAG022 | Architecture | Warning | Aggregate services from one variation
RAG023 | Architecture | Warning | Keep service methods instance-based
RAG024 | Architecture | Warning | Place files in Exposures, Models, Services, or Brokers folders except Program.cs
RAG025 | Architecture | Warning | Keep models as data carriers with auto-properties only
RAG026 | Architecture | Warning | Place enums in a folder under Models
RAG027 | Architecture | Warning | Keep IServiceCollection registration files at the project root
RAG028 | Architecture | Warning | Use constructor injection instead of resolving services from a provider
RAG029 | Architecture | Warning | Use dependency injection instead of instantiating services, brokers, or exposures
RAG030 | Architecture | Warning | Keep System.IO.File usage in brokers, not services or exposures
