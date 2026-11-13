# Agent Rules for F1 Signature

1. **Never edit `.unity`, `.prefab`, `.asset`, or `.meta` files as text.**
   - Change scenes and prefabs only through Unity Editor scripts that use the official Unity API (`EditorSceneManager`, `PrefabUtility`, `SerializedObject`, `GameObject`, etc.), or through the Unity Inspector.
   - Text editing of Unity YAML easily produces broken PPtrs, dangling components, invalid File IDs, and corrupted scene hierarchies that break player builds.

2. **Never invent GUIDs or file IDs.**
   - All GUIDs and Local File Identifiers must be generated or assigned natively by Unity's internal serialization engine.

3. **Validate after changes:**
   - After any change to a scene or prefab, run the Scene Validator (`Tools > F1 Signature > Validate Scenes` or `SceneValidator.ValidateAllBuildScenes()`) and report its result.
   - Zero missing scripts and zero broken references must be verified before proceeding with builds.

