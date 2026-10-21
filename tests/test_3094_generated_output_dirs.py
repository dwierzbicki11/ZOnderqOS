from pathlib import Path


def test_generated_output_path():
    assert Path('obj/Release/net10.0/linux-x64/generated').parts[-1] == 'generated'
