#!/usr/bin/env bash
# Fetches the MOSS-TTS-Nano ONNX deployment once: ~770 MB, stored under
# artifacts/moss-tts/ and never committed. Without it the game runs silent.
#
#   tools/tts/fetch_models.sh
set -euo pipefail
cd "$(dirname "$0")/../.."

TTS=artifacts/moss-tts/MOSS-TTS-Nano-100M-ONNX
CODEC=artifacts/moss-tts/MOSS-Audio-Tokenizer-Nano-ONNX
mkdir -p "$TTS" "$CODEC"

fetch() {
  local repo="$1" dir="$2" file="$3"
  if [ -s "$dir/$file" ]; then
    echo "have    $dir/$file"
    return
  fi
  echo "fetch   $repo/$file"
  curl -fL --retry 3 -C - -o "$dir/$file" "https://huggingface.co/$repo/resolve/main/$file"
}

# The TTS side: the prefill and decode graphs share the global weights; the local
# (draft) graphs share theirs.
for file in \
    browser_poc_manifest.json \
    tts_browser_onnx_meta.json \
    tokenizer.model \
    moss_tts_prefill.onnx \
    moss_tts_decode_step.onnx \
    moss_tts_local_decoder.onnx \
    moss_tts_local_cached_step.onnx \
    moss_tts_local_fixed_sampled_frame.onnx \
    moss_tts_global_shared.data \
    moss_tts_local_shared.data; do
  fetch OpenMOSS-Team/MOSS-TTS-Nano-100M-ONNX "$TTS" "$file"
done

# The audio tokenizer: decode-only is all a narrator needs (the encoder is for
# cloning voices from recordings).
for file in \
    codec_browser_onnx_meta.json \
    moss_audio_tokenizer_decode_full.onnx \
    moss_audio_tokenizer_decode_shared.data \
    moss_audio_tokenizer_decode_step.onnx \
    moss_audio_tokenizer_encode.onnx \
    moss_audio_tokenizer_encode.data; do
  fetch OpenMOSS-Team/MOSS-Audio-Tokenizer-Nano-ONNX "$CODEC" "$file"
done

echo "done. try: dotnet run -c Release --project src/SolSystem.Speech -- --self-test"
