import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest


SCRIPT = Path(__file__).parents[2] / "scripts" / "unity-check.py"
SPEC = importlib.util.spec_from_file_location("unity_check", SCRIPT)
assert SPEC and SPEC.loader
unity_check = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(unity_check)


class UnityCheckTests(unittest.TestCase):
    def test_timeout_kills_nested_process_group(self):
        with tempfile.TemporaryDirectory() as directory:
            child_pid = Path(directory) / "child.pid"
            child_code = "import os,sys,time; open(sys.argv[1],'w').write(str(os.getpid())); time.sleep(60)"
            command = [sys.executable, "-c",
                       "import subprocess,sys,time; subprocess.Popen([sys.executable,'-c'," + repr(child_code) + ",sys.argv[1]], start_new_session=False); time.sleep(60)",
                       str(child_pid)]
            self.assertEqual(unity_check.run_process(command, env=os.environ.copy(), timeout=.5), 124)
            child = int(child_pid.read_text())
            for _ in range(20):
                try:
                    os.kill(child, 0)
                except ProcessLookupError:
                    break
                time.sleep(.05)
            else:
                self.fail("nested process survived timeout cleanup")

    def test_timeout_must_be_positive(self):
        with self.assertRaises(Exception):
            unity_check.positive_seconds("0")


if __name__ == "__main__":
    unittest.main()
