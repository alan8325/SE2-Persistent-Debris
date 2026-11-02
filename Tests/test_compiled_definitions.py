import json
import unittest
import uuid
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Assets"
VANILLA = Path(
    "/home/alan/.local/share/Steam/steamapps/common/Space Engineers 2 - Mod SDK"
    "/GameData/Vanilla/Content"
)
PARTIAL_TYPE = "VRage:Keen.VRage.ContentPipeline.Definitions.PartialDefinitionDiff"
PREFAB_TYPE = "VRage:Keen.VRage.Core.Game.Definitions.PrefabDefinition"
TIMEOUT_TYPE = "Game2:Keen.Game2.Simulation.WorldObjects.Shared.TimeoutComponentObjectBuilder"
PREFAB_CASES = (
    (
        "System/FractureDebris/Override of FractureDebris_Client.partialdef",
        "System/FractureDebris/FractureDebris_Client.def",
        "3cc73a40-9e48-43a9-885f-1c28bd5b824a",
    ),
    (
        "System/DamagedBlockDebris/Override of DamagedBlockDebrisPrefab_Client.partialdef",
        "System/DamagedBlockDebris/DamagedBlockDebrisPrefab_Client.def",
        "bef7011b-c06a-4be2-bf10-debc5bfe300c",
    ),
)


def load_json(path):
    with path.open(encoding="utf-8") as stream:
        return json.load(stream)


def timeout_change(partial):
    builders = partial["$Value"]["Manipulator"]["_entity"]["ObjectBuilders"]
    if not builders["$DeltaEncoded"]:
        raise AssertionError("ObjectBuilders must be delta encoded")
    if len(builders["Changed"]) != 1:
        raise AssertionError("Exactly one object-builder change is required")
    change = builders["Changed"][0]
    return builders, change


class SourceDefinitionTests(unittest.TestCase):
    def test_timeout_overrides_pause_only_the_existing_timeout(self):
        for partial_name, vanilla_name, base_guid in PREFAB_CASES:
            with self.subTest(partial=partial_name):
                partial = load_json(ASSETS / partial_name)
                vanilla = load_json(VANILLA / vanilla_name)
                self.assertEqual(PARTIAL_TYPE, partial["$Type"])
                self.assertEqual(base_guid, partial["$Value"]["BaseDefinition"])
                self.assertEqual("Override", partial["$Value"]["PartialDefinitionKind"])
                self.assertEqual(PREFAB_TYPE, partial["$Value"]["DefinitionType"])
                self.assertIs(False, partial["$Value"]["PriorityOverride"])

                builders, change = timeout_change(partial)
                self.assertEqual("Update", change["Kind"])
                self.assertEqual([], builders["Removed"])
                self.assertEqual(TIMEOUT_TYPE, change["Value"]["$Type"])
                self.assertIs(True, change["Value"]["TimeoutPaused"])

                timeout_key = builders["Keys"][change["Index"]]
                vanilla_timeout = next(
                    item["Value"]
                    for item in vanilla["$Value"]["_entity"]["ObjectBuilders"]
                    if item["Key"] == timeout_key
                )
                self.assertEqual(TIMEOUT_TYPE, vanilla_timeout["$Type"])
                self.assertEqual(vanilla_timeout["TimeToClose"], change["Value"]["TimeToClose"])

    def test_debris_cap_is_100_and_other_settings_are_preserved(self):
        partial = load_json(
            ASSETS / "System/Configurations/Override of FracturesDebrisPrefabsConfiguration.partialdef"
        )
        self.assertEqual(PARTIAL_TYPE, partial["$Type"])
        self.assertEqual("70563a82-6a30-4a58-af86-6b79f2ec8777", partial["$Value"]["BaseDefinition"])
        self.assertEqual("Override", partial["$Value"]["PartialDefinitionKind"])
        self.assertEqual({"MaxActiveDebris": 100}, partial["$Value"]["Manipulator"])

    def test_camera_debris_cleaner_component_is_removed(self):
        partial = load_json(
            ASSETS / "System/Camera/RenderCamera/Override of CompositeRenderCamera.partialdef"
        )
        vanilla = load_json(VANILLA / "System/Camera/RenderCamera/CompositeRenderCamera.def")
        self.assertEqual(PARTIAL_TYPE, partial["$Type"])
        self.assertEqual("5599ea50-642d-4f40-b5fb-2c61e38308c3", partial["$Value"]["BaseDefinition"])
        self.assertEqual("Override", partial["$Value"]["PartialDefinitionKind"])
        manipulator = partial["$Value"]["Manipulator"]
        self.assertEqual(["CameraDebrisCleanerComponent"], manipulator["TagSlots"]["RemovedKeys"])
        self.assertEqual([], manipulator["TagSlots"]["ModifiedKeys"])
        components = manipulator["Components"]
        self.assertEqual([], components["Changed"])
        self.assertEqual(["377f46ac-d20a-4525-a2ec-f8121f8c0f54"], components["Removed"])
        self.assertEqual(
            [
                item["Key"]
                for item in vanilla["$Value"]["Components"]
                if item["Key"] != "377f46ac-d20a-4525-a2ec-f8121f8c0f54"
            ],
            components["Keys"],
        )
        self.assertFalse((ASSETS / "System/Camera/RenderCamera/Override of CameraDebrisCleanerDefinition.partialdef").exists())

    def test_camera_cleaner_is_also_removed_from_prefab(self):
        partial = load_json(
            ASSETS / "System/Camera/RenderCamera/Override of PrefabRenderCamera.partialdef"
        )
        vanilla = load_json(VANILLA / "System/Camera/RenderCamera/PrefabRenderCamera.def")
        self.assertEqual(PARTIAL_TYPE, partial["$Type"])
        self.assertEqual("8a6b77be-837d-4866-a8a8-cadebcf78dd1", partial["$Value"]["BaseDefinition"])
        self.assertEqual("Override", partial["$Value"]["PartialDefinitionKind"])
        builders = partial["$Value"]["Manipulator"]["_entity"]["ObjectBuilders"]
        self.assertEqual([], builders["Changed"])
        self.assertEqual(["377f46ac-d20a-4525-a2ec-f8121f8c0f54"], builders["Removed"])
        self.assertEqual(
            [
                item["Key"]
                for item in vanilla["$Value"]["_entity"]["ObjectBuilders"]
                if item["Key"] != "377f46ac-d20a-4525-a2ec-f8121f8c0f54"
            ],
            builders["Keys"],
        )

    def test_collision_conflict_cleanup_is_disabled(self):
        partial = load_json(
            ASSETS / "System/FractureDebris/Override of DebrisDefinition.partialdef"
        )
        self.assertEqual(PARTIAL_TYPE, partial["$Type"])
        self.assertEqual("5e81b064-2aab-45f2-a3bf-e05db6b7418e", partial["$Value"]["BaseDefinition"])
        self.assertEqual("Override", partial["$Value"]["PartialDefinitionKind"])
        self.assertEqual({"ConflictingTicksToDestroy": 2147483647}, partial["$Value"]["Manipulator"])

    def test_all_assets_and_folders_have_world_metadata(self):
        paths = [ASSETS]
        paths.extend(path for path in ASSETS.rglob("*") if path.is_dir() or path.suffix == ".partialdef")
        for path in paths:
            with self.subTest(asset=path.relative_to(ASSETS)):
                metadata_path = path.with_name(path.name + ".meta")
                self.assertTrue(metadata_path.is_file(), f"Missing {metadata_path.name}")
                metadata = load_json(metadata_path)
                self.assertEqual("VRage:Keen.VRage.ContentPipeline.Metafiles.MetaData", metadata["$Type"])
                uuid.UUID(metadata["$Value"]["AssetID"])
                definition_sets = [
                    item.get("DefinitionSetName")
                    for item in metadata["$Value"]["InternalItems"]
                    if item["$Type"] == "VRage:Keen.VRage.ContentPipeline.Definitions.DefinitionSetItem"
                ]
                self.assertIn("World", definition_sets)


if __name__ == "__main__":
    unittest.main()
