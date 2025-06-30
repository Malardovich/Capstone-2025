import socket
import time
import threading

class ClientUDP(threading.Thread):

    def __init__(self, ip, port, autoReconnect=True) -> None:
        threading.Thread.__init__(self)
        self.ip = ip
        self.port = port
        self.autoReconnect = autoReconnect
        self.connected = False
        self.running = True
        self.socket = None

    def run(self):
        self.connect()
        while self.running:
            time.sleep(0.1)  # Mantener el hilo vivo

    def isConnected(self):
        return self.connected

    def sendMessage(self, message):
        if not self.connected or not self.socket:
            print("Not connected. Message not sent.")
            return
        try:
            message = str('%s<EOM>' % message).encode('utf-8')
            self.socket.send(message)
        except (ConnectionRefusedError, ConnectionResetError, OSError) as ex:
            print(f"Connection error: {ex}")
            self.disconnect()
        except Exception as ex:
            print(f"Unexpected error: {ex}")
            self.disconnect()

    def disconnect(self):
        self.connected = False
        if self.socket:
            try:
                self.socket.close()
            except Exception:
                pass
            self.socket = None
        if self.autoReconnect and self.running:
            time.sleep(1)
            self.connect()

    def connect(self):
        try:
            self.socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            print("Attempting Connection...")
            self.socket.connect((self.ip, self.port))
            print("Will send messages to " + str(self.socket.getpeername()))
            self.connected = True
        except (ConnectionRefusedError, ConnectionResetError, OSError) as ex:
            print(f"Connection error: {ex}")
            self.disconnect()
        except Exception as ex:
            print(f"Unexpected error: {ex}")
            self.disconnect()

    def stop(self):
        self.running = False
        self.disconnect()