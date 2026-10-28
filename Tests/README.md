# Export regression fixture

Use an isolated Unity 2022.3 project with the VRChat SDK and this package installed.
Copy the files in `Editor/` into the package's `Editor` folder so they can
exercise the internal exporter, then run:

```sh
Unity -batchmode -nographics -projectPath /path/to/isolated/project \
  -executeMethod RemoteOSC.Unity.Editor.Tests.AvatarExportRegression.Run \
  -logFile /path/to/export-regression.log
```

The fixture creates temporary avatar objects and output directories. It checks
menu exclusions, parameter renaming, menu renaming, editor-only component removal,
unresolved mappings, preview cleanup on success/failure, suppression of preview
exports, and generated parameter exclusions across build-time renaming. Success
exits with code 0; failures exit with code 1.
The fixture is outside the distributable package and adds no runtime dependency.

With VRCFury and Modular Avatar installed, also run the same command with
`-executeMethod RemoteOSC.Unity.Editor.Tests.AvatarGeneratedToolsRegression.Run`.
This creates real VRCFury and MA components, checks generated discovery and source
asset preservation, exercises Inspector permission serialization, then verifies
the exclusions through the SDK build callbacks. It also checks that the Inspector
preview remains usable after generation and permission edits, and becomes stale
after source hierarchy edits. The fixture uses reflection so
the package keeps no hard dependency on either optional tool.
