# KAT Nexus Protocol (v2.1.5)

KAT Nexus device runs KAT Gateway with up to 8 devices exposing them via UDP local network.

# Status Broadcast

Broadcast packets sent every 1s with nexus device status, which allows discovery and automatic connection by the client.

Broadcast packet goes to port 1181:

  - 0x84 0x11 -- magic header
  - char[16]  -- IPv4 of nexus
  - u32       -- number of connected treadmills/devices
  - treadmill data

Treadmill data: (KATDeviceStatus, 116 bytes each)

  - double -- lastUpdate (double since start)
  - u16 -- pid
  - u16 -- vid
  - char[13] -- serial no
  - u8 -- sensor 1 connected [direction]
  - u8 -- sensor 2 connected
  - u8 -- sensor 3 connected
  - float32 -- sensor 1 battery [direction]
  - float32 -- sensor 2 battery
  - float32 -- sensor 3 battery
  - char[3] -- ?? // error?
  - u32 -- s1 packets/sec
  - u32 -- s2 packets/sec
  - u32 -- s3 packets/sec
  - char[16] -- client IP
  - char[8] -- ??
  - u32 -- nexus port
  - u8 -- number of client ports
  - u32 x 8 -- client ports

Nexus client tries to connect to the Nexus packet that has treadmill with sensors connected.

# Connection Protocol

Each treadmill connected to nexus box gets port from range 3500 to 3600, announced in the status packet.

The client sends UDP packets to that port, and nexus box sends answers to given port back.

There are could be up to 8 client threads connected from the single IP, every status updates are sent to each
client port registered.


## The communication commands

Format (259 bytes min):

  - 0x54 0x0B (decibal 84 11) -- magic header
  - u8 -- Command Byte
  - the rest is Arguments

Commands:
  - 0x00 -- Ping; argument: at offset 11 -- (int) Port No
  
    Example: 54 0B 00 00 00 00 00 00 00 00 00 11 22 00 00 -- ping, send respond to port 8721

    If there is no clients talking to device, client + given port is remembered, connection is established.
  
    Answer: 54 0B 01 00 00 00 00 00 00 00 00 dd dd dd dd dd dd dd dd -- active pong, dd...dd -- (Double) current time
  
    Answer: Command changed to 1; at offset 11 written current time
  
    Note: current time is seconds from Unix Epoch

    If there is already clients, and the ip:port is not one of the clients, client got rejected:


    Answer: 54 0B 16 02 00 00 00 00 00 00 00 dd dd dd dd dd dd dd dd -- reject pong, dd...dd -- (Double) current time
  
    Command changed to 22, at offset 11 written current time

  - 0x01 -- Pong; argument: at offset 11 -- (Double) current time, at offset 19 -- (int) Port No
  
    Example: 54 0b 01 00 00 00 00 00 00 00 dd dd dd dd dd dd dd dd 11 22 00 00 -- PONG, my time is dd...dd, my port is 8721

    NO answer

  - 0x0A -- SyncConfig
  
    whole packet is NexusDeviceConfig, 260 bytes (including 0x54 0x0B 0x0A header)

    -  offset 0x03 (+  3) -- KAT_DRIVER_CONFIG_ struct
    -  offset 0x46 (+ 70) -- KAT_INPUT_CONFIG_ struct
    -  offset 0x58 (+ 88) -- KAT_DEVICE_CALIBRATION_CONFIG_ struct
    -  offset 0x6d (+109) -- KAT_CALIBRATIONDATA_ struct
    -  offset 0x75 (+117) -- checked for asciiz serialNo to match the connection

  - 0x14 -- Connect; arguments: at offset 3 -- (int) Port No, at offset 7 (bool) force connect
  
    Possible states:
  
    -  No connection        : connected, response 00 00
    -  Already connected    : connected, response 00 03
    -  Too many connections :  rejected, response 01 02
    -  Busy with another IP :  rejected, response 01 0B  [can be overriden with force connect, then old IP get disconnect]

    Answer: 54 0B 15 {response} (int)ServerPort (Ascii-z)(Message or Serial No)

  - 0x17 -- Disconnect by client; at offset 3 (int) Port

    No answer

  - 0x50 -- Vibrate, at offset 3 (Float) Intensity

    No answer

  - 0x51 -- Set LED, at offset 3 (Float) Intensity
  
    No answer


## Status update packets

For every connected client port (up to 8), the data status packet is sent on every change (packet from treadmill or timeout read from treadmill).
Data sent if port sent ping within last 3 seconds. If no ping longer than 5 seconds, port is dropped.

Status update packet: [GetTreadmillData]

  - Prefix: 11 bytes
  
    - 54 0B 63  (84 11 99 decimal)
    - uint64    packetNo (incremented with each update)

  - TreadmillData: 250 bytes
