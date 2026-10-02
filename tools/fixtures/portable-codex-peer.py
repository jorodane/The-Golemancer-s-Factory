#!/usr/bin/env python3
"""Explicit protocol fixture for project history portability; never a shipped provider or a real model."""
import json
import os
from pathlib import Path
import sys
import uuid

home = Path(os.environ['CODEX_HOME'])
log = Path(os.environ['PACKENGINE_TEST_LOG'])
mode = os.environ.get('PACKENGINE_TEST_MODE', 'normal')
thread = ''

def send(x):
    print(json.dumps(x), flush=True)

def path(id):
    matches = list((home / 'sessions').rglob('rollout-*-' + id + '.jsonl'))
    assert len(matches) == 1, ('missing native history', id)
    return matches[0]

def records(id):
    return [json.loads(x) for x in path(id).read_text().splitlines()]

def summary(id):
    rows = records(id)
    questions = [r['payload']['content'][0]['text'] for r in rows if r['type'] == 'response_item' and r['payload']['role'] == 'user']
    return {'id': id, 'path': '' if mode == 'archive-fail' else str(path(id)), 'cwd': rows[0]['payload']['cwd'], 'originator': 'packengine_editor', 'preview': questions[0] if questions else '', 'status': {'type': 'notLoaded'}}

for line in sys.stdin:
    req = json.loads(line)
    with log.open('a') as out:
        out.write(json.dumps(req) + '\n')
    method, a = req.get('method'), req.get('params', {})
    if method == 'initialized':
        continue
    if method == 'initialize':
        result = {'userAgent': 'PORTABILITY FIXTURE'}
    elif method == 'account/read':
        result = {'account': {'type': 'chatgpt', 'email': 'fixture@example.invalid', 'planType': 'plus'}}
    elif method == 'config/read':
        result = {'config': {}}
    elif method == 'thread/start':
        thread = str(uuid.uuid4())
        file = home / 'sessions' / '2026' / '10' / '01' / ('rollout-2026-10-01T00-00-00-' + thread + '.jsonl')
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(json.dumps({'type': 'session_meta', 'payload': {'id': thread, 'cwd': os.getcwd(), 'originator': 'packengine_editor'}}) + '\n')
        result = {'thread': summary(thread)}
    elif method == 'thread/resume':
        thread = a['threadId']
        assert a['sandbox'] == 'read-only' and a['approvalPolicy'] == 'never' and a['config']['features.shell_tool'] is False
        assert len(records(thread)) >= 3, 'restored the ID but lost the conversation'
        result = {'thread': summary(thread)}
    elif method == 'thread/read':
        result = {'thread': summary(a['threadId'])}
    elif method == 'thread/list':
        result = {'data': [], 'nextCursor': None}
    elif method == 'thread/turns/list':
        turns = []
        for record in records(a['threadId']):
            if record['type'] != 'response_item':
                continue
            p = record['payload']; text = p['content'][0]['text']
            if p['role'] == 'user':
                turns.append({'id': str(len(turns)), 'status': 'completed', 'items': [{'type': 'userMessage', 'content': [{'type': 'text', 'text': text}]}]})
            else:
                turns[-1]['items'].append({'type': 'agentMessage', 'text': text})
        result = {'data': list(reversed(turns)), 'nextCursor': None}
    elif method == 'turn/start':
        thread = a['threadId']; turn = str(uuid.uuid4()); text = a['input'][0]['text']
        with path(thread).open('a') as out:
            for role, kind, body in [('user', 'input_text', text), ('assistant', 'output_text', 'PORTABILITY_FIXTURE_OK')]:
                out.write(json.dumps({'type': 'response_item', 'payload': {'type': 'message', 'role': role, 'content': [{'type': kind, 'text': body}]}}) + '\n')
        send({'id': req['id'], 'result': {'turn': {'id': turn, 'status': 'inProgress'}}})
        send({'method': 'turn/started', 'params': {'threadId': thread, 'turn': {'id': turn, 'status': 'inProgress'}}})
        if mode == 'disconnect':
            sys.exit(0)
        if mode == 'cancel':
            log.with_suffix('.ready').write_text('ready')
            continue
        send({'method': 'item/completed', 'params': {'threadId': thread, 'turnId': turn, 'item': {'type': 'agentMessage', 'id': 'final', 'text': 'PORTABILITY_FIXTURE_OK', 'phase': 'final_answer'}}})
        send({'method': 'turn/completed', 'params': {'threadId': thread, 'turn': {'id': turn, 'status': 'completed', 'items': []}}})
        continue
    elif method == 'turn/interrupt':
        result = {}
    else:
        raise AssertionError(req)
    send({'id': req['id'], 'result': result})
