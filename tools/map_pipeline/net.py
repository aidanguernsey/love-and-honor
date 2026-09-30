"""Shared download settings."""
from __future__ import annotations

import hashlib
import json
import os

# No personal data by default. OSM's usage policy appreciates a contact in the User-Agent; set
# LH_PIPELINE_CONTACT (e.g. an email or URL) in your environment if you want to include one.
_contact = os.environ.get("LH_PIPELINE_CONTACT", "").strip()
USER_AGENT = "LoveAndHonor-map-pipeline/0.1 (non-commercial fan project" + (f"; {_contact}" if _contact else "") + ")"


def cached_name(kind: str, request: dict | str, suffix: str) -> str:
    """Stable cache file name for a request, so re-runs reuse downloads."""
    blob = request if isinstance(request, str) else json.dumps(request, sort_keys=True)
    return f"{kind}_{hashlib.sha1(blob.encode('utf-8')).hexdigest()[:12]}{suffix}"
