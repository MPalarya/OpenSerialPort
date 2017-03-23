        private System.Timers.Timer _timeoutTimer = null;

        private void portInitData(List<byte> data, out int lenToRead)
        {
            if (_timeoutTimer != null && _timeoutTimer.Enabled)
                _timeoutTimer.Stop();

            lock (data)
            {
                lenToRead = -1;
                data.Clear();
            }
        }

        private void portResetTimer()
        {
            if (_timeoutTimer != null)
            {
                if (_timeoutTimer.Enabled)
                    _timeoutTimer.Stop();

                _timeoutTimer.Start();
            }
        }

        public void Open()
        {
            if (port != null)
            {
                port.Open();

                // once the port is open we begin reading infinitely 
                // everything that we read is appended to a List<byte>
                // we begin loop-reading until we get first 2 bytes - indicating length of msg
                // having the length, we know when to stop reading and raise the outer event
                // each incomplete message initiates a timeout-timer

                int lenToRead = -1;
                List<byte> data = new List<byte>();

                portInitData(data, out lenToRead);

                // define timer
                _timeoutTimer = new System.Timers.Timer(1000) { AutoReset = false };
                _timeoutTimer.Elapsed += delegate(object sender, ElapsedEventArgs e)
                {
                    portInitData(data, out lenToRead);
                };

                byte[] buffer = new byte[1024];
                Action kickoffRead = null;
                kickoffRead = delegate
                {
                    try
                    {
                        port.BaseStream.BeginRead(buffer, 0, buffer.Length,
                            delegate(IAsyncResult ar)
                            {
                                try
                                {
                                    int currentReadLength = port.BaseStream.EndRead(ar);
                                    
                                    byte[] receivedBytes = new byte[currentReadLength];
                                    Buffer.BlockCopy(buffer, 0, receivedBytes, 0, currentReadLength);
                                    lock (data)
                                    {
                                        data.AddRange(receivedBytes);

                                        // we don't have enough bytes to deduce lenToRead
                                        if (data.Count < 2)
                                        {
                                            // error - not possible, lenToRead is deduced by data
                                            if (lenToRead >= 2)
                                                portInitData(data, out lenToRead);

                                            // have read some bytes, but not enough
                                            else
                                                // not finished yet - not enough bytes to deduce lenToRead
                                                portResetTimer();
                                        }

                                        // we read enough bytes to know lenToRead
                                        else
                                        {
                                            if (lenToRead < 2)
                                            {
                                                // get lenToRead
                                                byte[] lenArray = new byte[] { data[0], data[1] };
                                                lenToRead = EndianBitConverter.Little.ToInt16(lenArray, 0);

                                                // error - length must be > 2
                                                if (lenToRead < 2)
                                                    portInitData(data, out lenToRead);
                                            }

                                            // either it was already > 2, or we just calculated it in previous 'if'
                                            // careful not to use 'else' here:
                                            if (lenToRead >= 2)
                                            {
                                                if (lenToRead == data.Count && this.BytesReceived != null)
                                                {
                                                    byte[] tmpArray = data.ToArray();

                                                    portInitData(data, out lenToRead);

                                                    // finished - raise event
                                                    BytesReceived(this, tmpArray);
                                                }

                                                // not finished yet - still bytes to read until lenToRead
                                                else if (lenToRead > data.Count)
                                                    portResetTimer();

                                                // error - read more than we should, msg is invalid
                                                else
                                                    portInitData(data, out lenToRead);
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {

                                }
                                kickoffRead();
                            }, null);
                    }
                    catch (Exception ex)
                    {
                        // try to "reconnect" again after 1 sec
                        Thread.Sleep(1000);
                        if ((port != null) && (!port.IsOpen))
                        {
                            try
                            {
                                port.Open();
                            }
                            catch (Exception exx)
                            {

                            }
                        }
                        kickoffRead();
                    }
                };
                kickoffRead();
            }
        }
