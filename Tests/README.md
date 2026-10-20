# Export regression fixture

Use an isolated Unity 2022.3 project with the VRChat SDK and this package installed.
Copy `Editor/AvatarExportRegression.cs` into the package's `Editor` folder so it can
exercise the internal exporter, then run:

```sh
Unity -batchmode -nographics -projectPath /path/to/isolated/project \
  -executeMethod RemoteOSC.Unity.Editor.Tests.AvatarExportRegression.Run \
  -logFile /path/to/export-regression.log
```

The fixture creates temporary avatar objects and output directories. It checks
menu exclusions, parameter renaming, menu renaming, editor-only component removal,
and unresolved mappings. Success exits with code 0; failures exit with code 1.
The fixture is outside the distributable package and adds no runtime dependency.
