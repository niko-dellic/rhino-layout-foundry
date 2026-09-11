"""Safety checks using disposable bundles only: python3 scripts/test-dev-install-macos.py."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("dev_install", Path(__file__).with_name("dev-install-macos.py"))
dev = importlib.util.module_from_spec(spec)
spec.loader.exec_module(dev)


class ProcessGuardTests(unittest.TestCase):
    def test_rhino_application_process_blocks_moves(self):
        result = dev.subprocess.CompletedProcess([], 0,
            "/Applications/Rhino 8.app/Contents/MacOS/Rhinoceros\n")
        with patch.object(dev.subprocess, "run", return_value=result):
            with self.assertRaises(RuntimeError):
                dev.require_rhino_closed()

    def test_unrelated_process_does_not_block(self):
        result = dev.subprocess.CompletedProcess([], 0, "/usr/bin/python3\n/bin/zsh\n")
        with patch.object(dev.subprocess, "run", return_value=result):
            dev.require_rhino_closed()


class BundleSafetyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.plugins = Path(self.temp.name) / "plugins"
        self.backup = Path(self.temp.name) / "backup"
        for name in dev.BUNDLES:
            bundle = self.plugins / name
            bundle.mkdir(parents=True)
            (bundle / "payload").write_bytes(name.encode())
        self.addCleanup(patch.stopall)
        patch.object(dev.sys, "platform", "darwin").start()
        self.closed = patch.object(dev, "require_rhino_closed").start()

    def test_round_trip_preserves_both_bundles_and_unrelated_data(self):
        unrelated = self.plugins / "unrelated.txt"
        unrelated.write_text("keep")
        dev.transfer("park", self.plugins, self.backup, True)
        self.assertFalse((self.plugins / dev.BUNDLES[0]).exists())
        dev.transfer("restore", self.plugins, self.backup, True)
        for name in dev.BUNDLES:
            self.assertEqual((self.plugins / name / "payload").read_bytes(), name.encode())
        self.assertEqual(unrelated.read_text(), "keep")

    def test_preview_makes_no_backup(self):
        dev.transfer("park", self.plugins, self.backup)
        self.assertFalse(self.backup.exists())
        self.assertTrue((self.plugins / dev.BUNDLES[0]).exists())

    def test_conflict_preflights_entire_pair(self):
        (self.backup / dev.BUNDLES[1]).mkdir(parents=True)
        with self.assertRaises(RuntimeError):
            dev.transfer("park", self.plugins, self.backup, True)
        self.assertTrue((self.plugins / dev.BUNDLES[0]).exists())

    def test_running_rhino_prevents_changes(self):
        self.closed.side_effect = RuntimeError("Rhino running")
        with self.assertRaises(RuntimeError):
            dev.transfer("park", self.plugins, self.backup, True)
        self.assertFalse(self.backup.exists())

    def test_nested_backup_rejected(self):
        with self.assertRaises(RuntimeError):
            dev.transfer("park", self.plugins, self.plugins / "backup", True)

    def test_second_move_failure_rolls_back_first(self):
        original = dev.os.rename
        calls = 0

        def fail_second(src, dst):
            nonlocal calls
            calls += 1
            if calls == 2:
                raise OSError("simulated failure")
            original(src, dst)

        with patch.object(dev.os, "rename", side_effect=fail_second):
            with self.assertRaises(OSError):
                dev.transfer("park", self.plugins, self.backup, True)
        for name in dev.BUNDLES:
            self.assertEqual((self.plugins / name / "payload").read_bytes(), name.encode())

    def test_symlink_bundle_rejected(self):
        self.backup.mkdir()
        (self.backup / dev.BUNDLES[0]).symlink_to(self.plugins / dev.BUNDLES[0])
        with self.assertRaises(RuntimeError):
            dev.transfer("restore", self.plugins, self.backup, True)


if __name__ == "__main__":
    unittest.main()
