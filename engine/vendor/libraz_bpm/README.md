Source: https://github.com/libraz/bpm-detector
Commit: 8d31a95bbd4120890e7d545b9aac029942a0df29
License: MIT (see LICENSE).

Only BPMDetector and its constants were extracted from music_analyzer.py into tempo.py. Five key detector modules are unchanged. Package __init__ is minimal to avoid importing unrelated analysis dependencies. The host adapter gates silence/noise/low confidence and rejects upstream fallback guesses; it analyzes at most three 30-second windows. Tested with existing librosa 0.11 / NumPy / SciPy. No upstream package installer is executed.
