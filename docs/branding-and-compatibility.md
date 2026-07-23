# Branding and compatibility

The project is named **QuestIonAble File Manager** from version `0.4.0`.

Canonical identifiers:

- product: `QuestIonAble File Manager`;
- repository and Pages path: `QuestIonAble-File-Manager`;
- .NET projects and namespaces: `QuestIonAbleFileManager`;
- CLI executable: `questionable-file-manager.exe`;
- environment variables: `QUESTIONABLE_FILE_MANAGER_*`.

The capitalized `Ion` is part of the product spelling. The product remains an
independent open-source tool and is not affiliated with or endorsed by Meta.

## Update-safe compatibility

Some former identifiers remain deliberately:

- `MesmerPrism.MetaQuestFileManager` is the immutable signed Windows package
  identity used by existing `0.3.x` installations;
- `META_QUEST_FILE_MANAGER_ADB` and
  `META_QUEST_FILE_MANAGER_KIOSK_BUNDLE` are deprecated environment-variable
  fallbacks;
- rebranded releases include byte-identical former-name aliases for the setup,
  MSIX, App Installer, certificate, portable archive, and CLI archive;
- portable archives include `meta-quest-file-manager.exe` as a deprecated alias.

These are compatibility contracts, not public branding. New code,
documentation, links, and automation must use the canonical identifiers.
Historical Git commits, tags, `0.3.x` release titles, and immutable release
assets remain accurate records of what was published at the time.
