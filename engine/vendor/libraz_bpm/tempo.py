# Extracted BPMDetector from libraz/bpm-detector; see LICENSE and README.
from typing import Dict, List, Tuple
import numpy as np
import librosa

SR_DEFAULT = 22_050

HOP_DEFAULT = 128

BIN_WIDTH = 0.5

RATIOS = [0.5, 2 / 3, 0.75, 1.0, 4 / 3, 1.5, 2.0, 3.0, 4.0]

TOL = 0.05

THRESH_HIGHER = 0.15

class BPMDetector:
    """BPM detection using harmonic clustering."""

    def __init__(self, sr: int = SR_DEFAULT, hop_length: int = HOP_DEFAULT):
        self.sr = sr
        self.hop_length = hop_length

    def harmonic_cluster(self, bpms: np.ndarray, votes: np.ndarray) -> Dict[float, List[Tuple[float, int]]]:
        """Group BPM candidates into harmonic clusters."""
        clusters: Dict[float, List[Tuple[float, int]]] = {}
        for bpm, hit in sorted(zip(bpms, votes), key=lambda x: -x[1]):
            for base in list(clusters):
                r = bpm / base
                if any(abs(r - k) < TOL or abs(r - 1 / k) < TOL for k in RATIOS):
                    clusters[base].append((bpm, hit))
                    break
            else:
                clusters[bpm] = [(bpm, hit)]
        return clusters

    def smart_choice(self, clusters: Dict[float, List[Tuple[float, int]]], total_votes: int) -> Tuple[float, float]:
        """Choose the best BPM from clusters using smart selection."""
        # Handle empty clusters
        if not clusters or total_votes <= 0:
            return 120.0, 0.0  # Default BPM with zero confidence

        # base cluster = largest votes
        base, base_vals = max(clusters.items(), key=lambda kv: sum(v for _, v in kv[1]))
        base_votes = sum(v for _, v in base_vals)

        higher = [(rep, sum(v for _, v in vals)) for rep, vals in clusters.items() if rep > base]
        higher.sort(key=lambda x: -x[1])

        if higher and higher[0][1] / total_votes >= THRESH_HIGHER:
            # choose the representative BPM of the strongest higher cluster
            rep_bpm = higher[0][0]
            conf = 100 * higher[0][1] / total_votes
        else:
            if not base_vals:  # Additional safety check
                return 120.0, 0.0
            rep_bpm = max(base_vals, key=lambda x: x[1])[0]
            conf = 100 * base_votes / total_votes
        return rep_bpm, conf

    def detect(
        self, y: np.ndarray, sr: int, min_bpm: float = 40.0, max_bpm: float = 300.0, start_bpm: float = 150.0
    ) -> Tuple[float, float, np.ndarray, np.ndarray]:
        """Detect BPM from audio signal with optimized processing."""
        y_array = np.asarray(y)
        if y_array.size == 0 or not np.any(y_array):
            return 120.0, 0.0, np.array([], dtype=float), np.array([], dtype=int)

        # Optimize for speed: use smaller hop_length for faster processing
        optimized_hop = min(self.hop_length, 512)

        # Limit audio length for very long files to improve speed
        max_duration = 180  # 3 minutes max for BPM detection
        max_samples = max_duration * sr
        if len(y_array) > max_samples:
            # Use middle section for better representation
            start_idx = (len(y_array) - max_samples) // 2
            y_trimmed = y_array[start_idx : start_idx + max_samples]
        else:
            y_trimmed = y_array

        cands = librosa.feature.tempo(
            y=y_trimmed, sr=sr, aggregate=None, hop_length=optimized_hop, max_tempo=max_bpm, start_bpm=start_bpm
        )

        bins = np.arange(min_bpm, max_bpm + BIN_WIDTH, BIN_WIDTH)
        hist, edges = np.histogram(cands, bins=bins)
        top_idx = hist.argsort()[::-1][:10]
        top_idx = top_idx[hist[top_idx] > 0]
        top_bpms = edges[top_idx]
        top_hits = hist[top_idx]

        clusters = self.harmonic_cluster(top_bpms, top_hits)
        rep_bpm, conf = self.smart_choice(clusters, hist.sum())

        return rep_bpm, conf, top_bpms, top_hits
