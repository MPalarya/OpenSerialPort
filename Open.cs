        public void Open()
        {
            if (port != null)
            {
                port.Open();

                // once the port is open we begin reading infinitely 
                // everything that we read is appended to a List<byte>
                // we begin loop-reading until we get first 2 bytes - indicating length of msg
                // having the length, we know when to stop reading and raise the outer event
                // during this process we maintain a timeout timer

                int readSoFar = 0;
                int lenToRead = 0;
                bool abort = false;
                List<byte> data = new List<byte>();

                var timeout = new System.Timers.Timer(1000);
                timeout.Elapsed += delegate(object sender, ElapsedEventArgs e)
                {
                    lock (data)
                    {
                        abort = true;
                        readSoFar = 0;
                        lenToRead = -1; // make sure: lenToRead != readSoFar
                        data.Clear();
                    }
                };

                byte[] buffer = new byte[1024];
                Action kickoffRead = null;
                kickoffRead = delegate
                {
                    if (port != null && port.IsOpen)
                    {
                        try
                        {
                            abort = false;
                            timeout.Start();

                            port.BaseStream.BeginRead(buffer, 0, buffer.Length,
                                delegate(IAsyncResult ar)
                                {
                                    try
                                    {
                                        int actualReadLength = port.BaseStream.EndRead(ar);
                                        byte[] receivedBytes = new byte[actualReadLength];
                                        Buffer.BlockCopy(buffer, 0, receivedBytes, 0, actualReadLength);

                                        if (!abort)
                                        {
                                            lock (data)
                                            {
                                                data.AddRange(receivedBytes);
                                                readSoFar += actualReadLength;
                                                if (data.Count >= 2)
                                                {
                                                    byte[] lenArray = new byte[] { data[0], data[1] };
                                                    lenToRead = EndianBitConverter.Little.ToInt16(lenArray, 0); // TODO: is it little endian? <<< <<< <<<
                                                }
                                            }
                                        }

                                        // raise event
                                        if (readSoFar == lenToRead && this.BytesReceived != null)
                                            BytesReceived(this, data.ToArray());
                                    }
                                    catch (Exception ex)
                                    {
                                        // handle app serail error
                                    }
                                    kickoffRead();
                                }, null);
                        }
                        catch (Exception)
                        {
                            // try to "reconnect" again after 1 sec
                            Thread.Sleep(1000);
                            kickoffRead();
                        }
                    }
                };
                kickoffRead();
            }
        }
