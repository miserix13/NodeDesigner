# Plan: Stride Visual Designer MVP DRAFT

This plan implements your selected direction: Stride integration now, full interaction MVP, custom JSON graph format, and live in-app Node.js preview while targeting Win/Mac/Linux desktop backends. The approach starts with a Stride host spike to de-risk rendering/input integration before building the full editor stack. It keeps the current shell/navigation architecture and extends it with a dedicated designer route, then layers graph domain, interaction engine, undo/redo, inspector editing, persistence, and live generation in milestones that can be validated incrementally.

Steps

Add a dedicated designer route and page shell by extending RegisterRoutes and ConfigureServices in App.xaml.cs, and add NodeDesigner/Presentation/DesignerPage.xaml + NodeDesigner/Presentation/DesignerModel.cs while preserving Shell.xaml.
Implement a Stride host spike with an abstraction layer in NodeDesigner/Services/Rendering/IDesignerSurfaceHost.cs and NodeDesigner/Services/Rendering/StrideDesignerSurfaceHost.cs, then wire lifecycle into App.xaml.cs and desktop startup in Program.cs.
Add Stride dependencies and platform conditionals in NodeDesigner.csproj, including explicit backend capability checks for Windows/macOS/Linux and graceful fallback behavior when a backend is unsupported.
Define graph domain contracts in NodeDesigner/Models/Graph/GraphDocument.cs, NodeDesigner/Models/Graph/NodeModel.cs, NodeDesigner/Models/Graph/PortModel.cs, NodeDesigner/Models/Graph/EdgeModel.cs, plus viewport/selection state models.
Build persistence with schema versioning in NodeDesigner/Services/Persistence/GraphSerializer.cs and NodeDesigner/Services/Persistence/GraphDocumentStore.cs, and add save/load commands to NodeDesigner/Presentation/DesignerModel.cs.
Implement the editing engine in NodeDesigner/Services/Designer/InteractionController.cs for drag, port connect, pan/zoom, multi-select/delete, and event routing from NodeDesigner/Presentation/DesignerPage.xaml.
Add undo/redo with command pattern in NodeDesigner/Services/Designer/Commands/IDesignerCommand.cs and NodeDesigner/Services/Designer/UndoRedoStack.cs, then expose bindings in NodeDesigner/Presentation/DesignerModel.cs.
Add a properties inspector UI in NodeDesigner/Presentation/DesignerPage.xaml bound to selected graph elements via NodeDesigner/Presentation/DesignerModel.cs, including validation/coalesced property-change commands.
Implement live Node.js generation preview in NodeDesigner/Services/CodeGen/NodeJsGeneratorService.cs with debounced regeneration and preview binding in NodeDesigner/Presentation/DesignerModel.cs; add explicit export command for writing files.
Add focused tests for non-UI logic in NodeDesigner.Tests/NodeDesigner.Tests.csproj, especially serialization, undo/redo semantics, and generator output determinism; keep UI verification manual for first pass.
Verification

Run build task: build-desktop from NodeDesigner/.vscode/tasks.json.
Run publish task: publish-desktop from NodeDesigner/.vscode/tasks.json.
Manual checks: node drag, connect/disconnect edges, pan/zoom, marquee select, delete, undo/redo, property edits, live preview refresh, save/load round trip.
Cross-backend checks: startup and basic interaction smoke test on Windows/macOS/Linux desktop targets, with documented fallback path where Stride backend support differs.
Decisions

Stride integrated in v1, hosted embedded inside a designer page.
MVP includes drag/move, connect edges, pan/zoom, multi-select/delete, undo/redo, and properties panel.
Graph storage uses a custom JSON schema with explicit versioning.
Live generation uses in-app preview auto-refresh first, with explicit export for file output.
Target scope is all Uno desktop backends, with compatibility guards as part of the rendering milestone.