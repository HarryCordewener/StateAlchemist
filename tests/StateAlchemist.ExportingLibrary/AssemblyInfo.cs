// What a protocol package says about itself, in the one place C# allows it: an assembly attribute must precede
// every type in its file, so a library's offer lives here rather than beside the module.
[assembly: StateAlchemist.ExportsModule(typeof(StateAlchemist.ExportingLibrary.ExportedPingModule))]
