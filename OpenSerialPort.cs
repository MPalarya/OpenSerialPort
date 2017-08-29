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

        private void RestartTimer()
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

                                    lock (data)
                                    {
                                        for (int i = 0; i < currentReadLength; i++)
                                            data.Add(buffer[i]);

                                        // not enough bytes to deduce lenToRead
                                        if (data.Count < 2)
                                        {
                                            // error - cannot deduce lenToRead without reading first 2 bytes
                                            if (lenToRead >= 2)
                                                portInitData(data, out lenToRead);

                                            // not finished yet - not enough bytes to deduce lenToRead
                                            else
                                                RestartTimer();
                                        }

                                        // lenToRead is known
                                        else
                                        {
                                            if (lenToRead < 2)
                                            {
                                                lenToRead = EndianBitConverter.Little.ToInt16(new byte[] { data[0], data[1] }, 0);

                                                // error - already received 2 bytes, lenToRead must be >= 2
                                                if (lenToRead < 2)
                                                    portInitData(data, out lenToRead);
                                            }

                                            // either it was already > 2, or we just calculated it in previous 'if'
                                            // careful not to use 'else' here:
                                            if (lenToRead >= 2)
                                            {
                                                // finished
                                                if (lenToRead == data.Count && this.BytesReceived != null)
                                                {
                                                    BytesReceived(this, data.ToArray());
                                                    portInitData(data, out lenToRead);
                                                }

                                                // not finished yet - still have bytes to read until lenToRead
                                                else if (lenToRead > data.Count)
                                                    RestartTimer();

                                                // the actual msg is longer than we expected (data.Count > lenToRead)
                                                // handle case of multiple msgs concatenated by splitting
                                                else
                                                {
                                                    // handle each msg separately and restart timer
                                                    // if partial msg is left, let recursion handle it
                                                    while (data.Count >= lenToRead)
                                                    {
                                                        BytesReceived(this, data.GetRange(0, lenToRead).ToArray());
                                                        data.RemoveRange(0, lenToRead);

                                                        if (data.Count >= 2)
                                                            lenToRead = EndianBitConverter.Little.ToInt16(new byte[] { data[0], data[1] }, 0);
                                                        else
                                                            lenToRead = -1;
                                                        
                                                        // handled an entire msg and nothing left in list, start receiving again
                                                        if (data.Count == 0)
                                                            portInitData(data, out lenToRead);

                                                        RestartTimer();
                                                    }
                                                }
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
