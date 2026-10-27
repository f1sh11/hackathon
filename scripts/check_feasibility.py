#!/usr/bin/env python3
"""Read-only-by-default feasibility review client; uses only Python 3's standard library."""
import argparse
import json
import sys
import urllib.error
import urllib.request
import uuid
from pathlib import Path

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--backend', default='http://127.0.0.1:8787')
    parser.add_argument('--draft', required=True, type=Path)
    parser.add_argument('--data-source', choices=['direct', 'zetaris', 'zetaris-cloud'], default='direct')
    parser.add_argument('--requirements', type=Path)
    parser.add_argument('--plan-id', required=True)
    parser.add_argument('--plan-revision', type=int, required=True)
    parser.add_argument('--requirements-revision', type=int, required=True)
    args = parser.parse_args()
    body = dict(planId=args.plan_id, planRevision=args.plan_revision,
                requirementsRevision=args.requirements_revision,
                trip=json.loads(args.draft.read_text(encoding='utf-8')))
    body["trip"].pop("previousAdjustment", None)
    if args.requirements:
        body['requirements'] = json.loads(args.requirements.read_text(encoding='utf-8'))
    request = urllib.request.Request(args.backend.rstrip('/') + '/agents/feasibility/reviews',
        data=json.dumps(body, ensure_ascii=False).encode(), headers={'Content-Type': 'application/json', 'X-Planning-Run': str(uuid.uuid4()), 'X-Travel-Data-Source': args.data_source})
    try:
        with urllib.request.urlopen(request, timeout=240) as response:
            result = json.load(response)
    except urllib.error.HTTPError as error:
        print(json.dumps({'httpStatus': error.code, 'response': json.load(error)}, ensure_ascii=False, indent=2))
        return 1
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result.get('canConfirm') else 2

if __name__ == '__main__':
    sys.exit(main())
