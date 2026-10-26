# Third-party acknowledgements

StemForge is a native Windows adaptation inspired by these projects, not an official release of them.

- StemDeck, https://github.com/stemdeckapp/stemdeck, Apache-2.0. Reference snapshot `5f9405ca68ac31b31ae07ac416141d4d1f7b5f3a`. The Demucs normalization/inference sequence in engine/engine.py is adapted from app/pipeline/demucs_worker.py. Native WPF UI, process bridge and drum processing are new.
- SUNO-AI-refix-audio, https://github.com/haystudio73/SUNO-AI-refix-audio, snapshot `e02365582f15ddf2fbde3dac20320a3383343aad`. src/utils/audioEngine.ts carries SPDX-License-Identifier: Apache-2.0. Its EQ frequencies, Q values and signal topology informed the Python refix implementation. No React UI is redistributed in the executable.
- Meta Demucs, https://github.com/facebookresearch/demucs, MIT; model weights downloaded by Demucs on first use. PyTorch BSD-style; NumPy BSD; SciPy BSD; SoundFile BSD and its libsndfile dependency LGPL. Installed separately in Python, not bundled in this release.
- FFmpeg, https://ffmpeg.org, LGPL/GPL depending on the selected build. This project invokes a separate executable; the published app archive does not bundle FFmpeg. The optional Settings installer downloads Gyan's GPLv3 Windows release essentials build from https://www.gyan.dev/ffmpeg/builds/, verifies its SHA-256, and retains the package's notices and documentation in the user's tool directory. Gyan links corresponding source on its builds page.
- OpenAI Whisper, https://github.com/openai/whisper, MIT. The lyrics module invokes openai-whisper (optional install pinned to 20250625); code and model weights are installed/downloaded separately, not bundled in the app archive. Recognition runs locally. Dependencies retain their respective licenses.
- .NET/WPF, Microsoft, MIT. The default published build is framework-dependent.
- Microsoft.Data.Sqlite.Core 9.0.17, .NET Foundation and Contributors, MIT; full notice in licenses/Microsoft.Data.Sqlite.txt. SQLitePCLRaw 2.1.10, Copyright 2014-2024 SourceGear, LLC, Apache-2.0 (full license in licenses/Apache-2.0.txt). The bundled SQLite engine is public domain. These SQLite dependencies are shipped in the v0.2 application folder, including their native runtime files.

Suno Audio Forge is distributed under GPL-3.0 (LICENSE). The complete Apache-2.0 text for upstream components is retained in licenses/Apache-2.0.txt. The two requested repositories are cloned unmodified into upstream/ for inspection; their own license files and source headers remain there. See their exact source trees for notices. Do not assume all third-party dependencies share the application's license.

Eventide SplitEQ and Zynaptiq UNMIX::DRUMS are functional references for the stereo workflows. No proprietary algorithms, plugin binaries, product UI, samples or branding are distributed. engine/stereo_dsp.py independently implements shared STFT masks, a percussive/tonal heuristic, spectral gain, Mid/Side control and an onset-triggered sub oscillator using NumPy/SciPy. It is not Eventide Structural Split, Zynaptiq drum unmixing, or a bit-identical replacement for either plugin. The application supports live RAM previews and full-file offline export.

Puncher 2 Lite and Steven Slate Drums Trigger 2 (https://stevenslatedrums.com/trigger2) are functional references only. No proprietary DSP, binaries, graphics, branding or samples are included. The built-in drum sounds are synthesized by this project. The onset gate, refractory interval, cross-band rejection, velocity mapping and sample bank playback are independent implementations. Cross-band rejection is not Trigger 2's spill-track suppression. Drum Lab is an offline effect/trigger processor, not a VST host or a bit-identical recreation of a commercial plugin.


## Embedded guide reader (0.9.1)

Microsoft.Web.WebView2 SDK 1.0.4022.49, Microsoft Corporation (https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4022.49). SDK binaries and native loader are redistributed under the license in licenses/Microsoft.Web.WebView2.txt; upstream third-party notices are in licenses/Microsoft.Web.WebView2-NOTICE.txt. The Microsoft Edge WebView2 Runtime is a separate prerequisite, not included in the application archive. Guide HTML and its image are embedded resources of StemForge; no remote guide content is loaded.

## Optional SW / DrumSep models (0.6)

Tempo/key analysis (0.9) uses librosa 0.11.0 (https://github.com/librosa/librosa), ISC, installed separately through engine/requirements.txt. Its dependencies retain their respective licenses. StemForge's key estimator correlates harmonic chroma with Krumhansl–Kessler major/minor profiles; estimates are not a certified key/tempo benchmark.

The app calls audio-separator 0.47.0 (https://github.com/nomadkaraoke/python-audio-separator, MIT) installed separately. This package includes the MDXC/TFC-TDF and RoFormer inference adapters; their upstream source/dependency notices apply. No inference package, model weight or third-party recorded drum sample is bundled in the app archive.

- BS-RoFormer SW Fixed: https://huggingface.co/enerjazzer/BS-ROFO-SW-Fixed, revision `a443a2985534b3bc815ef54a5d446c6a0390f974`. The mirror identifies its license as unknown; training provenance and commercial-use rights are not verified here. Do not treat the application's GPL-3.0 license as a license grant for this checkpoint.
- MDX23C DrumSep (aufr33 / jarredou), listed at https://github.com/ZFTurbo/Music-Source-Separation-Training/blob/main/docs/pretrained_models.md. Pinned download mirror: https://huggingface.co/Politrees/UVR_resources/tree/929e057b81aa49bc2e6490bef8671f47b2c120f6/models/MDX23C . Checkpoint and configuration SHA-256 hashes are recorded and enforced in engine/model_pipeline.py. Mirror metadata is not proof of rights to all training materials.

DrumSep is MDX23C, not the KimberleyJensen Mel-Band vocal model. No claims of a 150-track evaluation or reproduced SDR benchmark are made. MIDI serialization and six-lane transient detection are independently implemented by StemForge.


## libraz/bpm-detector (analysis subset)

Source: https://github.com/libraz/bpm-detector
Pinned commit: `8d31a95bbd4120890e7d545b9aac029942a0df29`. MIT license reproduced in `engine/vendor/libraz_bpm/LICENSE`. BPMDetector and its constants are extracted into tempo.py; five key-analysis modules are unmodified. Package initialization is minimal. Suno Audio Forge adds bounded-window analysis, confidence/silence/noise gates and rejects fallback guesses. Uses the existing librosa/NumPy/SciPy dependencies. See the vendored README for scope.


## Suno public lyrics integration

The user-provided Suno Lyric Downloader 3.1.1 extension was inspected as a behavioral reference for public clip metadata and aligned-lyrics response fields. Its proprietary source is not copied, linked or redistributed. `engine/suno_lyrics.py` is an independent implementation using public HTTPS requests, without account cookies or tokens. API availability and public timestamps are controlled by Suno.

Version 0.14.1 adds an independently implemented, user-initiated authenticated alignment request inside a dedicated Suno WebView2 profile. The page's session is used only within that browser to call Suno's API. Neither the extension's code nor browser-account credentials are bundled. Clerk's session API is documented at https://clerk.com/docs/js-frontend/reference/objects/session . Availability of login and aligned lyrics remains subject to Suno and the sign-in provider.

## NAudio live playback (0.15.0)

NAudio.Core and NAudio.Wasapi 2.2.1 are bundled for shared-mode WASAPI PCM playback. Copyright 2020 Mark Heath, MIT license; full notice in licenses/NAudio.txt. Source and license: https://github.com/naudio/NAudio/tree/v2.2.1 and https://raw.githubusercontent.com/naudio/NAudio/v2.2.1/license.txt . Live DSP and the bounded RAM worker are implemented by Suno Audio Forge; NAudio supplies the Windows playback adapter.
