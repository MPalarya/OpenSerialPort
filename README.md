# PortOpen
bypassing SerialPort's BytesToRead property (which is completely unreliable) in .NET4, using port's BaseStream property instead.
This hack is based on the excellent article by Ben Voigt from this blog:
http://www.sparxeng.com/blog/software/must-use-net-system-io-ports-serialport

in case the article will ever go down, it is cited here:

As an embedded developer who writes desktop software mostly for configuration of, and data download from, peripheral devices, I use serial data streams a lot.  Mostly USB virtual serial posts from FTDI, but also the USB Communication Device Class and real 16550-compatible UARTs on the PCI bus.  Since looking at data through an in-circuit emulator debug interface is generally    a miserable experience, getting serial data communication with a custom PC application is essential to analyzing data quality and providing feedback on hardware designs.  C# and the .NET Framework provide a rapid application development that is ideal for early development that needs to track changing requirements as hardware designs evolve.  Ideal in most respects, I should say.

The System.IO.Ports.SerialPort class which ships with .NET is a glaring exception.  To put it mildly, it was designed by computer scientists operating far outside their area of core competence.  They neither understood the characteristics of serial communication, nor common use cases, and it shows.  Nor could it have been tested in any real world scenario prior to shipping, without finding flaws that litter both the documented interface and the undocumented behavior and make reliable communication using System.IO.Ports.SerialPort (henceforth IOPSP) a real nightmare.  (Plenty of evidence on StackOverflow attests to this, from devices that work in Hyperterminal but not .NET because IOPSP makes setting certain parameters mandatory, although they aren’t applicable to virtual ports, and closes the port on failure.  There’s no way to bypass or ignore failure of these settings during IOPSP initialization.)

What’s even more astonishing is that this level of failure occurred when the underlying kernel32.dll APIs are immensely better (I’ve used the WinAPI before working with .NET, and still do when I want to use a function that .NET doesn’t have a wrapper for, which notably includes device enumeration).  The .NET engineers not only failed to devise a reasonable interface, they chose to disregard the WinAPI design which was very mature, nor did they learn from two decades of kernel team experience with serial ports.

A future series of posts will present the design and implementation of a rational serial port interface built upon, and preserving the style of, the WinAPI serial port functions.  It fits seamlessly into the .NET event dispatch model, and multiple coworkers have expressed that it’s exactly how they want a serial-port class to work.  But I realize that external circumstances sometimes prohibit using a C++/CLI mixed-mode assembly.  The C++/CLI solution is incompatible with:

Partial trust (not really a factor, since IOPSP’s Open method also demands UnmanagedCode permission)
Single-executable deployment (there may be workarounds involving ILMerge or using netmodules to link the C# code into the C++/CLI assembly)
Development policies that prohibit third-party projects
.NET Compact Framework (no support for mixed-mode assemblies)
The public license (as yet undetermined) might also present a problem for some users.

Or maybe you are responsible for improving IOPSP code that is already written, and the project decision-maker isn’t ready to switch horses.  (This is not a good decision, the headaches IOPSP will cause in future maintenance far outweigh the effort of switching, and you’ll end up switching in the end to get around the unfixable bugs.)

So, if you fall into one of these categories and using the Base Class Library is mandatory, you don’t have to suffer the worst of the nightmare.  There are some parts of IOPSP that are a lot less broken that the others, but that you’ll never find in MSDN samples.  (Unsurprisingly, these correspond to where the .NET wrapper is thinnest.)  That isn’t to say that all the bugs can be worked around, but if you’re lucky enough to have hardware that doesn’t trigger them, you can get IOPSP to work reliably in limited ways that cover most usage.

I planned to start with some guidance on how to recognize broken IOPSP code that needs to be reworked, and thought of giving you a list of members that should not be used, ever.  But that list would be several pages long, so instead I’ll list just the most egregious ones and also the ones that are safe.

The worst offending System.IO.Ports.SerialPort members, ones that not only should not be used but are signs of a deep code smell and the need to rearchitect all IOPSP usage:

The DataReceived event (100% redundant, also completely unreliable)
The BytesToRead property (completely unreliable)
The Read, ReadExisting, ReadLine methods (handle errors completely wrong, and are synchronous)
The PinChanged event (delivered out of order with respect to every interesting thing you might want to know about it)
Members that are safe to use:

The mode properties: BaudRate, DataBits, Parity, StopBits, but only before opening the port. And only for standard baud rates.
Hardware handshaking control: the Handshake property
Port selection: constructors, PortName property, Open method, IsOpen property, GetPortNames method
And the one member that no one uses because MSDN gives no example, but is absolutely essential to your sanity:

The BaseStream property
The only serial port read approaches that work correctly are accessed via BaseStream.  Its implementation, the System.IO.Ports.SerialStream class (which has internal visibility; you can only use it via Stream virtual methods) is also home to the few lines of code which I wouldn’t choose to rewrite.

Finally, some code.

Here’s the (wrong) way the examples show to receive data:

```
port.DataReceived += port_DataReceived;

// (later, in DataReceived event)
try {
    byte [] buffer = new byte[port.BytesToRead];
    port.Read(buffer, 0, buffer.Length);
    raiseAppSerialDataEvent(buffer);
}
catch (IOException exc) {
    handleAppSerialError(exc);
} 
```

Here’s the right approach, which matches the way the underlying Win32 API is intended to be used:

```
byte[] buffer = new byte[blockLimit];
Action kickoffRead = null;

kickoffRead = delegate {
port.BaseStream.BeginRead(buffer, 0, buffer.Length, 
        delegate (IAsyncResult ar) {
            try {
                int actualLength = port.BaseStream.EndRead(ar);
                byte[] received = new byte[actualLength];
                Buffer.BlockCopy(buffer, 0, received, 0, actualLength);
                raiseAppSerialDataEvent(received);
            }
            catch (IOException exc) {
                handleAppSerialError(exc);
            }
            kickoffRead();
        }, null);
    };
    kickoffRead();
```

It looks like a little bit more, and more complex code, but it results in far fewer p/invoke calls, and doesn’t suffer from the unreliability of the BytesToRead property.  (Yes, the BytesToRead version can be adjusted to handle partial reads and bytes that arrive between inspecting BytesToRead and calling Read, but those are only the most obvious problems.)

Starting in .NET 4.5, you can instead call ReadAsync on the BaseStream object, which calls BeginRead and EndRead internally.

Calling the Win32 API directly we would be able to streamline this even more, for example by reusing a kernel event handle instead of creating a new one for each block.  We’ll look at that issue and many more in future posts exploring the C++/CLI replacement.
