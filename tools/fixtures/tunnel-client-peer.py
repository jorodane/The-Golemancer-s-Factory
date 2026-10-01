#!/usr/bin/python3
"""Local process/health fixture only. No OpenAI traffic, authentication, or MCP proof."""
import http.server
import json
import os
from pathlib import Path
import shlex
import subprocess
import sys
import time

assert sys.argv[1:3] == ['run', '--profile-file']
profile = Path(sys.argv[3])
config = json.loads(profile.read_text())
command = shlex.split(config['mcp']['commands'][0]['command'])
mode = Path(command[2]).name
if mode == 'exit.packproject':
    sys.exit(17)
if mode == 'slow.packproject':
    time.sleep(60)
if mode == 'remote-health.packproject':
    Path(config['health']['url_file']).write_text('https://example.invalid/')
    time.sleep(60)


class Health(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200 if self.path == '/healthz' else 503)
        self.end_headers()
        self.wfile.write(b'fixture: process liveness only')

    def log_message(self, *args):
        pass


# Like PackEngine.Mcp, the downstream child owns a stdio session and exits on EOF.
child = subprocess.Popen(['/usr/bin/python3', '-c', 'import sys; sys.stdin.read()'], stdin=subprocess.PIPE)
server = http.server.HTTPServer(('127.0.0.1', 0), Health)
(profile.parent / 'observed.json').write_text(json.dumps(dict(
    keyWasChildEnvironment=os.environ.get('CONTROL_PLANE_API_KEY') == 'SYNTHETIC_TUNNEL_KEY_ONLY',
    argv=command, pid=os.getpid(), childPid=child.pid)))
Path(config['health']['url_file']).write_text(f'http://127.0.0.1:{server.server_port}')
server.serve_forever()
