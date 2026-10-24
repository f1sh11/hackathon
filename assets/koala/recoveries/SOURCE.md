# Existing return sequences

Imported without pixel edits from `Koala_Recovery_Only_5_Actions.zip`.

- Archive SHA-256: `0b8e7c8160c48cc7b5c21d4af8a3e0dfbfab6552157f0a4ea1ba00608aa88848`
- Weather: 26 frames; map: 30; budget: 32; tired: 36. All are 256 × 256 RGBA at 12 FPS.
- Supplied provenance is `source_frame_reversal`: existing complete action cels played as a return sequence, rather than a newly drawn recovery. Each sequence begins at its corresponding held endpoint and ends at canonical idle.
- The archive also contains a departure approximation using a dissolve. That sequence is intentionally excluded: the suitcase must remain visible until a physical recovery is available.
- The original `recovery` string remains intact for older clients. iOS can consume optional `recoveryId` and `recoveries` metadata; older Android builds ignore those additions.
