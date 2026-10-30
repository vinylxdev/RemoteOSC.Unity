# RemoteOSC.Unity

Unity avatar exporter for RemoteOSC.
Exports final VRChat avatar parameters, expression menus, and sharing permissions
as `<avatarId>.yaml` after avatar build transformations.

## Install

Requires Unity 2022.3 and the VRChat Avatars SDK 3.7.0 or newer.

Install using VCC or ALCOM using my [repository](https://www.vinyl.dev/projects#vpm-title) link:
```
https://www.vinyl.dev/vpm/index.json
```

## Use

1. Add **RemoteOSC → Avatar Config Exporter** to the avatar root.
2. Use **Export location → Browse…** to choose the client's avatar config folder.
   An empty location writes into `Assets/`. Browse starts in the client's saved
   avatar config folder when it can be found.
3. The component list appears automatically without running avatar build tools.
   It inspects existing parameters/controllers, MA parameter declarations and menu
   components, and VRCFury toggles, sliders and Full Controller assets. Use
   **Refresh component list** to rescan. Names marked **Generated name** are
   resolved during the actual avatar build.
4. Use **Remote permissions** to choose the menu controls and parameters friends
   may use. Search, filter, **Allow shown**, and **Block shown** apply to the
   displayed editable entries. Generated parameters without a discoverable menu
   binding require the optional exact preview before they can be edited.
   **Generate exact preview (optional)** processes a temporary copy to show the
   complete final list. **Use component list** returns to component inspection.
5. Use **Advanced exclusions** for exact parameter names or stable menu paths,
   including controls generated during the build. Excluding a menu also excludes
   its children. System parameters and animator triggers stay protected.
6. Build the avatar. An avatar without a blueprint ID must be uploaded once and
   then built again before its config can be exported.

Component exclusions retain references to their source components, including when
the hierarchy or menu labels change. Existing parameter-name and menu-path rules
remain supported. Final parameter names and permissions are exported after the
normal avatar build transformations. Denied
parameters remain in YAML with `allowed: false`; the client cannot re-enable them.
An unresolved denied parameter blocks the export until its mapping is reviewed.
