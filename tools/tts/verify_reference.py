#!/usr/bin/env python3
"""The reference ONNX orchestrator, run against the same bundle, to answer one question:
is the C# port's synthesis speed the hardware's, or the port's waste?

Needs the user-site packages (pip install --user --break-system-packages onnxruntime numpy
sentencepiece) and the bundle under artifacts/moss-tts. Torch is NOT needed for this path:
built-in voices carry their prompt as codes in the manifest, so torchaudio is never touched —
but onnx_tts_runtime.py imports it eagerly, so this script stubs it first.
"""
import os, sys, types, time
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "vendor"))
# Stub the two imports the reference makes eagerly and this path never needs.
torch = types.ModuleType("torch"); torch.float32 = None
torch.Tensor = object
torchaudio = types.ModuleType("torchaudio")
torchaudio.load = lambda *a, **k: (_ for _ in ()).throw(NotImplementedError("no torch in the reference check"))
sys.modules["torch"] = torch; sys.modules["torchaudio"] = torchaudio

from onnx_tts_runtime import OnnxTtsRuntime

TEXT = ("Earth is dying. Two inheritors argue over what she left behind. "
        "This is not about who is right. It is about who gets to breathe.")

runtime = OnnxTtsRuntime(model_dir="artifacts/moss-tts", thread_count=int(sys.argv[1]) if len(sys.argv) > 1 else 4)
clock = time.perf_counter()
result = runtime.synthesize(
    text=TEXT, voice="Adam", prompt_audio_path="art/audio/narration/prompt.wav",
    output_audio_path="generated_audio/reference.wav", streaming=True,
    enable_wetext=False, enable_normalize_tts_text=False, seed=1234)
clock = time.perf_counter() - clock
wave = result["waveform"]
frames = result["audio_token_ids"].shape[0]
print(f"reference: {frames} frames = {frames*0.08:.2f} s of audio in {clock:.2f} s "
      f"= {frames*0.08/clock:.2f}x realtime")
