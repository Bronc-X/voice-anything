using System.Globalization;

namespace SayAll.HidBridge.Contracts;

public static class GadgetScriptBuilder
{
    private const string Template = """
        const MODULE_NAME = "Microsoft.Bluetooth.Profiles.HidOverGatt.dll";
        const MODULE_SIZE = 233472;
        const CAPTURE_RVA = 0x15980;
        const CAPTURE_BYTES = "b918000000e826cafeff";
        const REPORT_ARRIVED_RVA = 0x20720;
        const REPORT_ARRIVED_BYTES = "4c894424188854241055";
        const REPORT_LENGTH = 6;
        const MAX_REPORT_LENGTH = 64;
        const PROTOCOL_VERSION = 1;
        const TOKEN = "__TOKEN__";
        const HOST = "127.0.0.1";
        const PORT = __PORT__;
        const RECONNECT_DELAY_MS = 500;

        let output = null;
        let reconnectTimer = null;
        let writeChain = Promise.resolve();
        let hookInstalled = false;

        function asciiBytes(text) {
          const result = [];
          for (let index = 0; index < text.length; index++) {
            result.push(text.charCodeAt(index) & 0xff);
          }
          return result;
        }

        function hex(pointer, length) {
          if (pointer.isNull() || length <= 0) return "";
          const bytes = new Uint8Array(pointer.readByteArray(length));
          let result = "";
          for (let index = 0; index < bytes.length; index++) {
            result += bytes[index].toString(16).padStart(2, "0");
          }
          return result;
        }

        function scheduleReconnect() {
          if (reconnectTimer !== null) return;
          reconnectTimer = setTimeout(() => {
            reconnectTimer = null;
            connectToBridge();
          }, RECONNECT_DELAY_MS);
        }

        function markDisconnected(currentOutput) {
          if (output !== currentOutput) return;
          output = null;
          scheduleReconnect();
        }

        function emit(payload) {
          const currentOutput = output;
          if (currentOutput === null) return;
          const envelope = Object.assign({
            version: PROTOCOL_VERSION,
            token: TOKEN
          }, payload);
          const line = JSON.stringify(envelope) + "\n";
          writeChain = writeChain
            .then(() => currentOutput.writeAll(asciiBytes(line)))
            .catch(() => markDisconnected(currentOutput));
        }

        async function connectToBridge() {
          if (output !== null) return;
          try {
            const connection = await Socket.connect({
              family: "ipv4",
              host: HOST,
              port: PORT
            });
            output = connection.output;
            emit({ kind: "ready", hook_installed: hookInstalled });
          } catch (_error) {
            output = null;
            scheduleReconnect();
          }
        }

        function probe(source, reportId, payloadLength, reason) {
          emit({
            kind: "hook_probe",
            source: source,
            report_id: reportId,
            payload_length: payloadLength,
            reason: reason
          });
        }

        function inspectVector(source, reportId, vector) {
          if (vector.isNull()) {
            probe(source, reportId, -1, "empty_vector");
            return null;
          }
          const begin = vector.readPointer();
          const end = vector.add(Process.pointerSize).readPointer();
          if (begin.isNull() || end.isNull()) {
            probe(source, reportId, -1, "empty_vector");
            return null;
          }
          const length = end.sub(begin).toInt32();
          if (length <= 0 || length > MAX_REPORT_LENGTH) {
            probe(source, reportId, length, "invalid_length");
            return null;
          }
          if (reportId !== 1) {
            probe(source, reportId, length, "wrong_report_id");
            return null;
          }
          if (!(length === 6 || length === 7 || length === 8)) {
            probe(source, reportId, length, "invalid_length");
            return null;
          }
          probe(source, reportId, length, "accepted");
          return { begin: begin, length: length };
        }

        function installHooks() {
          if (hookInstalled) return;
          const module = Process.findModuleByName(MODULE_NAME);
          if (module === null || module.size !== MODULE_SIZE) {
            throw new Error("unsupported HidOverGatt module");
          }

          const capturePoint = module.base.add(CAPTURE_RVA);
          if (hex(capturePoint, 10) !== CAPTURE_BYTES) {
            throw new Error("unsupported HidOverGatt capture point");
          }
          const reportArrivedPoint = module.base.add(REPORT_ARRIVED_RVA);
          if (hex(reportArrivedPoint, 10) !== REPORT_ARRIVED_BYTES) {
            throw new Error("unsupported HidOverGatt ReportArrived point");
          }

          Interceptor.attach(capturePoint, {
            onEnter(_args) {
              try {
                const reportId = this.context.r15.toUInt32() & 0xff;
                const vector = this.context.rdi;
                inspectVector("value_changed", reportId, vector);
              } catch (_error) {
                probe("value_changed", 0, -1, "exception");
              }
            }
          });

          Interceptor.attach(reportArrivedPoint, {
            onEnter(_args) {
              const reportId = this.context.rdx.toUInt32() & 0xff;
              try {
                const owner = this.context.r8;
                if (owner.isNull()) {
                  probe("report_arrived", reportId, -1, "empty_owner");
                  return;
                }
                const vector = owner.readPointer();
                const inspected = inspectVector("report_arrived", reportId, vector);
                if (inspected === null) return;
                emit({
                  kind: "hid_report",
                  report_id: reportId,
                  payload: hex(inspected.begin, inspected.length)
                });
                inspected.begin.writeByteArray(new Uint8Array(inspected.length));
              } catch (_error) {
                probe("report_arrived", reportId, -1, "exception");
              }
            }
          });
          hookInstalled = true;
        }

        rpc.exports = {
          async init(_stage, _parameters) {
            await connectToBridge();
            installHooks();
            emit({ kind: "ready", hook_installed: true });
          }
        };
        """;

    public static string Build(int port, string sessionToken)
    {
        if (port is < 1 or > 65_535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        if (sessionToken.Length != 64 || !sessionToken.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Session token must be 32 bytes encoded as hex.", nameof(sessionToken));
        }

        return Template
            .Replace("__PORT__", port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__TOKEN__", sessionToken.ToUpperInvariant(), StringComparison.Ordinal);
    }
}
