from __future__ import annotations

import importlib.util
from pathlib import Path


SCRIPTS_DIR = Path(__file__).resolve().parents[1] / "scripts"


def _load_script(name: str):
    path = SCRIPTS_DIR / name
    spec = importlib.util.spec_from_file_location(path.stem, path)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_embedding_benchmark_parses_batch_sizes_and_percentiles():
    bench = _load_script("benchmark_embeddings.py")

    assert bench.parse_batch_sizes("1, 4,8") == [1, 4, 8]
    assert bench.percentile([3.0, 1.0, 2.0], 50) == 2.0
    assert bench.percentile([1.0, 2.0, 3.0, 4.0], 95) == 4.0


def test_localai_backend_script_extracts_model_ids():
    bench = _load_script("benchmark_localai_backends.py")

    data = {"data": [{"id": "nomic-embed-text-v1.5"}, {"id": "llama-3.1-8b-q4-k-m"}]}

    assert bench.model_ids(data) == {"nomic-embed-text-v1.5", "llama-3.1-8b-q4-k-m"}
