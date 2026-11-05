import importlib.util
import tempfile
import unittest
from pathlib import Path
from PIL import Image

spec = importlib.util.spec_from_file_location("shots", Path(__file__).parents[1] / "scripts" / "shots.py")
shots = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shots)


class ScreenshotTests(unittest.TestCase):
    def test_resize_keeps_ratio_and_refuses_upscaling(self):
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            source = directory / "source.png"
            Image.new("RGB", (160, 90), "navy").save(source)
            shots.save(source, directory / "look", [(80, "-sm"), (160, "")])
            with Image.open(directory / "look-sm.webp") as image:
                self.assertEqual(image.size, (80, 45))
            shots.save(source, directory / "native", [(0, "")])
            with Image.open(directory / "native.webp") as image:
                self.assertEqual(image.size, (160, 90))
            with self.assertRaises(ValueError):
                shots.save(source, directory / "look", [(320, "-2x")])
            self.assertFalse((directory / "look-2x.webp").exists())

    def test_missing_artifact_leaves_existing_assets_untouched(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(FileNotFoundError):
                shots.main(Path(directory))


if __name__ == "__main__":
    unittest.main()
