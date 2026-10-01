#!/usr/bin/env python3
"""Deterministic transport fixture, never a model/provider shipped with the editor."""
import json
import os
from pathlib import Path
import sys

mode = os.environ.get('PACKENGINE_TEST_MODE', 'edit')
log = Path(os.environ['PACKENGINE_TEST_LOG'])
thread = 'fixture-thread'
turn = 'fixture-turn'
next_id = 1000


def send(value):
    print(json.dumps(value, ensure_ascii=False), flush=True)


def receive():
    line = sys.stdin.readline()
    if not line:
        raise EOFError()
    value = json.loads(line)
    with log.open('a', encoding='utf-8') as stream:
        stream.write(json.dumps(value, ensure_ascii=False) + '\n')
    return value


def notify(method, **params):
    send({'method': method, 'params': {'threadId': thread, **params}})


def tool(name, arguments, success=True, wrong_turn=False):
    global next_id
    next_id += 1
    send({'id': next_id, 'method': 'item/tool/call', 'params': {'threadId': thread, 'turnId': 'wrong' if wrong_turn else turn,
          'callId': str(next_id), 'tool': name, 'arguments': arguments}})
    result = receive()
    assert result['id'] == next_id, result
    assert result['result']['success'] is success, result
    text = result['result']['contentItems'][0]['text']
    return json.loads(text) if success else text


try:
    while True:
        message = receive()
        method = message.get('method')
        params = message.get('params', {})
        if method == 'initialize':
            assert params['capabilities']['experimentalApi']
            send({'id': message['id'], 'result': {'userAgent': 'transport-fixture'}})
        elif method == 'initialized':
            pass
        elif method == 'account/read':
            account = {'type': 'apiKey'} if mode == 'api-key' else {'type': 'chatgpt', 'email': 'fixture@example.invalid', 'planType': 'plus'}
            send({'id': message['id'], 'result': {'account': account, 'requiresOpenaiAuth': True}})
        elif method == 'model/list':
            send({'id': message['id'], 'result': {'data': [{'model': 'fixture-model', 'displayName': 'Fixture', 'isDefault': True}], 'nextCursor': None}})
        elif method == 'thread/list':
            assert params['cwd'] == os.getcwd() and 'appServer' in params['sourceKinds']
            assert params['limit'] == 30 and params['sortKey'] == 'updated_at'
            entries = [{'id': 'fixture-older', 'cwd': os.getcwd(), 'originator': 'packengine_editor', 'preview': 'Earlier chat', 'updatedAt': 1}] if params.get('cursor') else [
                {'id': thread, 'cwd': os.getcwd(), 'originator': 'packengine_editor', 'preview': 'Saved question\n\n[Editor context captured when this request was sent]\n{}', 'updatedAt': 2},
                {'id': 'foreign', 'cwd': '/another/project', 'originator': 'packengine_editor', 'preview': 'DO NOT LIST'},
                {'id': 'other-client', 'cwd': os.getcwd(), 'originator': 'other-client', 'preview': 'DO NOT LIST'}]
            send({'id': message['id'], 'result': {'data': entries, 'nextCursor': None if params.get('cursor') else 'older-threads'}})
        elif method == 'thread/read':
            assert params['includeTurns'] is False
            id = params['threadId']
            send({'id': message['id'], 'result': {'thread': {'id': id, 'cwd': '/another/project' if id == 'foreign' else os.getcwd(),
                'originator': 'other-client' if id == 'other-client' else 'packengine_editor', 'status': {'type': 'active' if id == 'active' else 'notLoaded'}}}})
        elif method == 'thread/turns/list':
            assert params['limit'] == 10 and params['itemsView'] == 'full' and params['sortDirection'] == 'desc'
            send({'id': message['id'], 'result': {'data': [
                {'id': 'recent', 'status': 'completed', 'items': [{'type': 'userMessage', 'content': [{'type': 'text', 'text': 'Recent question\n\n[Editor context captured when this request was sent]\n{}'}]}, {'type': 'agentMessage', 'text': 'RECENT_FIXTURE'}, {'type': 'reasoning', 'text': 'NOT CHAT'}]},
                {'id': 'old', 'status': 'completed', 'items': [{'type': 'userMessage', 'content': [{'type': 'text', 'text': 'Older question'}]}, {'type': 'agentMessage', 'text': 'OLDER_FIXTURE'}]}],
                'nextCursor': None if params.get('cursor') else 'older-messages'}})
        elif method == 'config/read':
            send({'id': message['id'], 'result': {'config': {'mcp_servers': {'fixture_external': {'enabled': True}}}}})
        elif method in ('thread/start', 'thread/resume'):
            assert params['sandbox'] == 'read-only' and params['approvalPolicy'] == 'never'
            assert params['config']['features.shell_tool'] is False
            assert params['config']['mcp_servers']['fixture_external']['enabled'] is False
            assert params['modelProvider'] == 'openai'
            assert params['config']['features.unified_exec'] is False and params['config']['web_search'] == 'disabled'
            assert params['cwd'].endswith('codex-workspace')
            if method == 'thread/start':
                assert params['environments'] == []
                names = {tool['name'] for tool in params['dynamicTools']}
                assert len(names) == 8 and 'packengine_patch' in names and 'packengine_editor' in names
                assert all(tool['type'] == 'function' for tool in params['dynamicTools'])
            else:
                assert params['threadId'] in (thread, 'fixture-older') and params['excludeTurns']
                thread = params['threadId']
            send({'id': message['id'], 'result': {'thread': {'id': thread}}})
        elif method == 'turn/start':
            assert params['threadId'] == thread and params['environments'] == []
            snapshot = json.loads(params['input'][0]['text'].split('[Editor context captured when this request was sent]\n', 1)[1])
            send({'id': message['id'], 'result': {'turn': {'id': turn, 'status': 'inProgress', 'items': []}}})
            notify('turn/started', turn={'id': turn, 'status': 'inProgress', 'items': []})
            if mode == 'disconnect':
                sys.exit(0)
            if mode == 'cancel':
                log.with_suffix('.ready').write_text('ready')
                continue
            if mode == 'failure':
                notify('turn/completed', turn={'id': turn, 'status': 'failed', 'error': {'message': 'FIXTURE_FAILURE'}})
                continue
            if mode == 'edit':
                assert snapshot['Input']['Mode'] == 'single' and len(snapshot['Context']) == 1
                target = snapshot['Input']['Targets'][0]
                assert target['Key'] == 'widget:golemancer.costButton'
                assert snapshot['WritablePacks'] == ['golemancer.controls']
                assert 'not belong' in tool('packengine_find', {'query': 'buy'}, False, True)
                matches = tool('packengine_find', {'query': 'commerce.buy'})
                assert matches['Total'] > 0
                relations = tool('packengine_inspect', {'key': target['Key'], 'section': 'relations'})
                assert 'golemancer.button' in relations['Content']
                parent = tool('packengine_inspect', {'key': 'widget:golemancer.button', 'section': 'definition'})
                assert '<Widget' in parent['Content']
                read = tool('packengine_read', {'path': target['File'], 'startLine': 1, 'lineCount': 80})
                assert read['DocumentHash'] == snapshot['Context'][0]['DocumentHash']
                tool('packengine_build', {'pack': 'commerce'}, False)
                tool('packengine_project', {'operation': 'verify'}, False)
                send({'id': 9999, 'method': 'item/commandExecution/requestApproval', 'params': {'threadId': thread, 'turnId': turn}})
                assert receive()['result']['decision'] == 'decline'
                patch = {'path': target['File'], 'expectedHash': 'stale', 'oldText': 'property="fontSize" value="13"',
                         'newText': 'property="fontSize" value="15"', 'intent': 'transport fixture only'}
                tool('packengine_patch', patch, False)
                patch['expectedHash'] = read['DocumentHash']
                preview = tool('packengine_patch', patch)
                assert preview['Applied'] is False
                assert tool('packengine_apply', {'changeId': preview['ChangeId']})['Applied']
                assert tool('packengine_build', {'pack': 'golemancer.controls'})['Completed']
            else:
                assert snapshot['Input']['Mode'] == 'none' and snapshot['Context'] == []
            # Stale turn messages must not contaminate this response.
            notify('item/agentMessage/delta', turnId='old-turn', itemId='old', delta='STALE')
            notify('turn/completed', turn={'id': 'old-turn', 'status': 'completed', 'items': []})
            notify('item/agentMessage/delta', turnId=turn, itemId='final', delta='FIXTURE_')
            notify('item/agentMessage/delta', turnId=turn, itemId='final', delta='PROTOCOL_OK')
            notify('item/completed', turnId=turn, item={'type': 'agentMessage', 'id': 'final', 'text': 'FIXTURE_PROTOCOL_OK', 'phase': 'final_answer'})
            notify('turn/completed', turn={'id': turn, 'status': 'completed', 'items': [], 'error': None})
        elif method == 'turn/interrupt':
            send({'id': message['id'], 'result': {}})
            notify('turn/completed', turn={'id': turn, 'status': 'interrupted', 'items': []})
        else:
            raise AssertionError(message)
except EOFError:
    pass
