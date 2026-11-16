# Echo code conventions

- The desktop is a native macOS SwiftUI/AppKit application. Use SceneKit/Metal for 3D viewing. Do not add Avalonia or another cross-platform desktop framework.
- The CLI and processing subsystems are C#. Keep each subsystem in its own project.
- Put one named type in each file, with the filename matching the type. Do not hide helper classes inside another class.
- Use MEF (`System.ComponentModel.Composition`) for application services, I/O, resource planning and subsystem boundaries. Export meaningful service interfaces, use explicit `[ImportingConstructor]` constructors and readonly dependency fields. Resolve services only in the application composition root. Do not create interfaces or MEF exports for stateless calculation helpers merely to make them injectable.
- Do not introduce static services, mutable global state, or primary constructors for classes. Stateless algorithm helpers such as `RandomSampleSelector` may be static: keep all inputs, including random generators, explicit and do not compose them with MEF. C# operator declarations are also permitted.
- Name fields in lowerCamelCase without an underscore prefix. Use `this.` to disambiguate constructor assignments.
- Use named types for domain data; do not expose positional tuples as contracts.
- Use Allman braces, expanded control blocks, one statement per line, and blank lines between logical steps. Follow `.editorconfig`.
- Extract meaningful boolean groups for conditions with three or more predicates, retaining short-circuit and null-guard behavior.
- Separate run coordination, checkpoint persistence, algorithm execution, and UI state. Prefer focused methods to long command switches or methods containing several processing stages.
- Preserve serialized artifact contracts and algorithm fingerprints during behavior-preserving cleanup. Change the algorithm version when reconstruction behavior changes.
- Run `scripts/check-format.sh`, the C# tests, and the native macOS build for changes affecting both applications. Inspect native viewers against saved artifacts when changing rendering.
- Keep the README focused on durable product, setup, usage, development and licensing information. Do not add session progress, implementation diaries, temporary artifacts, one-off benchmark results or exhaustive internal details. Report work and validation in the chat or PR; do not create tracked documentation merely to relocate those notes.
