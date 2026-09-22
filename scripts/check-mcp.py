"""Exercise the shipped MCP process with temporary local data, never a user's journal."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import queue
import subprocess
import tempfile
import threading
import uuid

parser = argparse.ArgumentParser()
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--helper', required=True)
parser.add_argument('--native', action='store_true', help='Run the published self-contained helper directly')
args = parser.parse_args()

with tempfile.TemporaryDirectory(prefix='voice-anything-mcp-') as temporary:
    token = 'AB' * 32  # Test fixture, never a real client grant.
    journal = json.loads((Path(__file__).resolve().parents[1] / 'docs/fixtures/journal-v1.json').read_text(encoding='utf-8'))
    journal['agentAccessEnabled'] = True
    journal['grants'] = [{'id': str(uuid.uuid4()), 'name': 'Process test',
                          'tokenHash': hashlib.sha256(token.encode()).hexdigest().upper(),
                          'createdAt': '2026-09-22T08:00:00Z'}]
    path = Path(temporary) / 'journal.json'
    path.write_text(json.dumps(journal, ensure_ascii=False), encoding='utf-8')
    environment = os.environ.copy()
    environment['VOICE_ANYTHING_MCP_TOKEN'] = token
    command = ([] if args.native else [args.dotnet]) + [str(Path(args.helper).resolve()), '--data-dir', temporary]
    process = subprocess.Popen(command,
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        text=True, encoding='utf-8', env=environment,
        creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
    responses = queue.Queue()
    def read_stdout():
        for line in process.stdout:
            responses.put(line)
        responses.put(None)
    threading.Thread(target=read_stdout, daemon=True).start()
    def send(value, expect=True):
        process.stdin.write((value if isinstance(value, str) else json.dumps(value)) + '\n')
        process.stdin.flush()
        if not expect:
            return None
        line = responses.get(timeout=10)
        assert line, 'MCP process exited before returning a response'
        return json.loads(line)
    try:
        answer = send({'jsonrpc': '2.0', 'id': 1, 'method': 'initialize',
                       'params': {'protocolVersion': '2025-06-18', 'capabilities': {}, 'clientInfo': {'name': 'test', 'version': '1'}}})
        assert answer['result']['protocolVersion'] == '2025-06-18'
        send({'jsonrpc': '2.0', 'method': 'notifications/initialized'}, expect=False)
        request = {'jsonrpc': '2.0', 'id': 2, 'method': 'tools/call',
                   'params': {'name': 'search_reflections', 'arguments': {'query': 'café'}}}
        answer = send(request)
        content = json.loads(answer['result']['content'][0]['text'])
        assert content['records'][0]['text'] == '共享记录 café：只保存本次新增的文字。'
        assert send('{broken')['error']['code'] == -32700
        assert send({'jsonrpc': '2.0', 'id': 3, 'method': 'ping'})['result'] == {}
        journal['grants'] = []
        replacement = path.with_suffix('.tmp')
        replacement.write_text(json.dumps(journal), encoding='utf-8')
        os.replace(replacement, path)
        assert send(request)['error']['code'] == -32001
        process.stdin.close()
        assert process.wait(timeout=10) == 0
        assert not process.stderr.read(), 'MCP emitted unexpected diagnostics'
        print('PASS real MCP process: handshake, Unicode search, malformed-request recovery, live revocation and clean exit')
    finally:
        if process.poll() is None:
            process.terminate()
            process.wait(timeout=5)
