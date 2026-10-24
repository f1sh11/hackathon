# Meterless H-MEM upstream

Source: https://github.com/Meterless/Meterless/tree/1742dec56a66e58eef9dda2d2b865823758ca8cd/engines/hmem

Commit: 1742dec56a66e58eef9dda2d2b865823758ca8cd

Vendored: reference/src, conformance, LICENSE. Conformance tests and license remain unchanged.
Runtime: Node.js 24 or newer, using native type stripping without transform flags.

Local compatibility patch (2026-10-24): constructor parameter properties in seven source files are expanded into explicit typed fields and constructor assignments. This preserves initialization, persistence and retrieval behavior while allowing Node 26, which removed TypeScript transformation support.
Official conformance: 13/13 passed.
This is the upstream deterministic reference engine, not a production service.
No automated sleep or dreaming job is enabled by Sydney Buddy.
