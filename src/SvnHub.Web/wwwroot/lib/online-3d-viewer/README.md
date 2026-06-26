# Online 3D Viewer Assets

This directory vendors the browser viewer used by SvnHub file previews.

- `o3dv.min.js` comes from `online-3d-viewer@0.18.0` (MIT).
- `occt/` comes from `occt-import-js@0.0.22` and is used for STEP, IGES, and BREP import.
- `rhino3dm/` comes from `rhino3dm@8.17.0` and is used for 3DM import.
- `web-ifc/` comes from `web-ifc@0.0.68` and is used for IFC import.
- `draco3d/` comes from `draco3d@1.5.7` and is used for Draco-compressed glTF import.
- The Online 3D Viewer bundle was patched to load all optional runtime libraries from `/lib/online-3d-viewer/` instead of public CDNs.

Supported direct preview formats are mapped in `ModelPreviewFileClassifier`.
