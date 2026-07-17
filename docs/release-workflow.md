# Release Workflow

GitHub Pages is the stable human-facing install surface. GitHub Releases is the
binary source of truth. Pages download links target `releases/latest/download`
so the website does not change for every version.

## Initial Delivery

The first release workflow will publish:

- `MetaQuestFileManager-win-x64.zip`;
- `meta-quest-file-manager-cli-win-x64.zip`;
- `SHA256SUMS.txt`.

The archives are self-contained .NET 10 Windows x64 publishes. The primary
archive contains both `MetaQuestFileManager.exe` and
`meta-quest-file-manager.exe`; this makes every command displayed by the GUI
runnable from the same extracted directory. The second archive remains a
CLI-only automation download. Android Platform Tools are not bundled in this
first lane.

## Signed Guided Delivery

The next delivery unit will add:

- a privately signed guided setup executable;
- a signed MSIX and `.appinstaller` feed if package identity is justified;
- the public certificate needed for the preview trust bootstrap;
- startup update checks against GitHub Releases;
- a portable fallback archive;
- signature, timestamp, package-shape, and installed-launch validation.

Private keys stay in the Windows certificate store and GitHub Actions secrets.
They are never committed. A self-issued certificate can support an explicitly
trusted MSIX but does not guarantee that Smart App Control will admit a
downloaded helper executable. The manual certificate and App Installer route
must remain available until a publicly trusted signing path is configured.

No release tag should be published until the exact package shape and update
identity are tested on a clean Windows machine.
