using PlutoGE.ScriptCore;
namespace CoD.Scripts;

// Project compatibility facade; navigation and device hints are engine-owned.
public sealed class ControllerMenu(RmlDocument document) : PlutoGE.ScriptCore.ControllerMenu(document);
