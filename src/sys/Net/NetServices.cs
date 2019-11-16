// ============================================================================
// FileName: NetServices.cs
//
// Description:
// Contains wrappers to access the functionality of the underlying operating
// system.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Dec 2005	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Sys
{
    /// <summary>
    /// Helper class to provide network services.
    /// </summary>
    public class NetServices
    {
        public const int UDP_PORT_START = 1025;
        public const int UDP_PORT_END = 65535;
        private const int RTP_RECEIVE_BUFFER_SIZE = 100000000;
        private const int RTP_SEND_BUFFER_SIZE = 100000000;
        private const int MAXIMUM_RTP_PORT_BIND_ATTEMPTS = 5;               // The maximum number of re-attempts that will be made when trying to bind the RTP port.

        private static ILogger logger = Log.Logger;

        private static Mutex _allocatePortsMutex = new Mutex();

        public static UdpClient CreateRandomUDPListener(IPAddress localAddress, out IPEndPoint localEndPoint)
        {
            return CreateRandomUDPListener(localAddress, UDP_PORT_START, UDP_PORT_END, null, out localEndPoint);
        }

        public static void CreateRtpSocket(IPAddress localAddress, int startPort, int endPort, bool createControlSocket, out Socket rtpSocket, out Socket controlSocket)
        {
            rtpSocket = null;
            controlSocket = null;

            lock (_allocatePortsMutex)
            {
                // Make the RTP port start on an even port as the specification mandates. 
                // Some legacy systems require the RTP port to be an even port number.
                if (startPort % 2 != 0)
                {
                    startPort += 1;
                }

                int rtpPort = startPort;
                int controlPort = (createControlSocket == true) ? rtpPort + 1 : 0;

                rtpPort = startPort;

                if (createControlSocket)
                {
                    controlPort = rtpPort + 1;
                }
                //}

                if (rtpPort != 0)
                {
                    bool bindSuccess = false;

                    for (int bindAttempts = 0; bindAttempts <= MAXIMUM_RTP_PORT_BIND_ATTEMPTS; bindAttempts++)
                    {
                        try
                        {
                            // The potential ports have been found now try and use them.
                            rtpSocket = new Socket(localAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                            rtpSocket.ReceiveBufferSize = RTP_RECEIVE_BUFFER_SIZE;
                            rtpSocket.SendBufferSize = RTP_SEND_BUFFER_SIZE;

                            rtpSocket.Bind(new IPEndPoint(localAddress, rtpPort));

                            if (controlPort != 0)
                            {
                                controlSocket = new Socket(localAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                                controlSocket.Bind(new IPEndPoint(localAddress, controlPort));

                                logger.LogDebug($"Successfully bound RTP socket {localAddress}:{rtpPort} and control socket {localAddress}:{controlPort}.");
                            }
                            else
                            {
                                logger.LogDebug($"Successfully bound RTP socket {localAddress}:{rtpPort}.");
                            }

                            bindSuccess = true;

                            break;
                        }
                        catch (System.Net.Sockets.SocketException sockExcp)
                        {
                            if (controlPort != 0)
                            {
                                logger.LogWarning($"Socket error {sockExcp.ErrorCode} binding to address {localAddress} and RTP port {rtpPort} and/or control port of {controlPort}, attempt {bindAttempts}.");
                            }
                            else
                            {
                                logger.LogWarning($"Socket error {sockExcp.ErrorCode} binding to address {localAddress} and RTP port {rtpPort}, attempt {bindAttempts}.");
                            }

                            // Increment the port range in case there is an OS/network issue closing/cleaning up already used ports.
                            rtpPort += 2;
                            controlPort += (controlPort != 0) ? 2 : 0;
                        }
                    }

                    if (!bindSuccess)
                    {
                        throw new ApplicationException("An RTP socket could be created due to a failure to bind on address " + localAddress + " to the RTP and/or control ports within the range of " + startPort + " to " + endPort + ".");
                    }
                }
                else
                {
                    throw new ApplicationException("An RTP socket could be created due to a failure to allocate on address " + localAddress + " and an RTP and/or control ports within the range " + startPort + " to " + endPort + ".");
                }
            }
        }

        public static UdpClient CreateRandomUDPListener(IPAddress localAddress, int start, int end, ArrayList inUsePorts, out IPEndPoint localEndPoint)
        {
            try
            {
                UdpClient randomClient = null;
                int attempts = 1;

                localEndPoint = null;

                while (attempts < 50)
                {
                    int port = Crypto.GetRandomInt(start, end);
                    if (inUsePorts == null || !inUsePorts.Contains(port))
                    {
                        try
                        {
                            localEndPoint = new IPEndPoint(localAddress, port);
                            randomClient = new UdpClient(localEndPoint);
                            break;
                        }
                        catch
                        {
                            //logger.LogWarning("Warning couldn't create UDP end point for " + localAddress + ":" + port + "." + excp.Message);
                        }

                        attempts++;
                    }
                }

                //logger.LogDebug("Attempts to create UDP end point for " + localAddress + ":" + port + " was " + attempts);

                return randomClient;
            }
            catch
            {
                throw new ApplicationException("Unable to create a random UDP listener between " + start + " and " + end);
            }
        }

        /// <summary>
        /// This method utilises the OS routing table to determine the local IP address to connection to a destination end point.
        /// The problem it is attempting to solve is selecting the correct local interface to use when communicating with another device
        /// on the same private network compared to a device on the Internet.
        /// See https://github.com/sipsorcery/sipsorcery/issues/97 for elaboration.
        /// </summary>
        /// <param name="destination">The remote destination to find a local IP address for.</param>
        /// <returns>The local IP address to use to connect to the remote end point.</returns>
        public static IPAddress GetLocalAddress(IPAddress destination)
        {
            UdpClient udpClient = new UdpClient(destination.AddressFamily);
            udpClient.Connect(destination, 0);
            return (udpClient.Client.LocalEndPoint as IPEndPoint).Address;
        }
    }
}