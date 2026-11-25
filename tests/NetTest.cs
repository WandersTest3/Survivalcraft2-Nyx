using Nyx;
using System.Diagnostics;
int fails=0; void Check(bool c,string m){ Console.WriteLine((c?"PASS ":"FAIL ")+m); if(!c) fails++; }
bool Wait(Func<bool> f,int ms=3000){ var sw=Stopwatch.StartNew(); while(sw.ElapsedMilliseconds<ms){ if(f()) return true; Thread.Sleep(10);} return false; }
var host=new NyxSession(true,1234,"Host"); host.StartHost(25599);
var a=new NyxSession(false,1234,"Ana"); a.StartClient("127.0.0.1",25599);
var b=new NyxSession(false,1234,"Beto"); b.StartClient("127.0.0.1",25599);
var bad=new NyxSession(false,999,"Errado"); bad.StartClient("127.0.0.1",25599);
var got=new List<Packet>();
Packet Next(NyxSession s,PacketType t){ Packet r=default; Wait(()=>{ while(s.Inbox.TryDequeue(out var i)){ if(i.packet.Type==t){ r=i.packet; return true; } } return false; }); return r; }
var w1=Next(a,PacketType.Welcome); var w2=Next(b,PacketType.Welcome);
Check(w1.Type==PacketType.Welcome && w2.Type==PacketType.Welcome && w1.Id!=w2.Id && w1.Id>0,"clientes recebem Welcome com ids distintos");
var rej=Next(bad,PacketType.Reject);
Check(rej.Type==PacketType.Reject && rej.Text.Contains("semente"),"semente diferente é recusada");
a.LocalId=w1.Id; b.LocalId=w2.Id; a.Connected=true; b.Connected=true;
a.SendToServer(new Packet{Type=PacketType.BlockChange,Cx=10,Cy=64,Cz=-5,Value=77});
var hostGot=Next(host,PacketType.BlockChange);
Check(hostGot.Cx==10&&hostGot.Cy==64&&hostGot.Cz==-5&&hostGot.Value==77,"host recebe BlockChange do cliente");
host.Broadcast(hostGot,w1.Id);
var bGot=Next(b,PacketType.BlockChange);
Check(bGot.Value==77&&bGot.Cx==10,"host retransmite para o outro cliente");
b.SendToServer(new Packet{Type=PacketType.PlayerState,Id=w2.Id,X=1.5f,Y=2.5f,Z=3.5f,Qw=1f});
var ps=Next(host,PacketType.PlayerState);
Check(ps.X==1.5f&&ps.Y==2.5f&&ps.Z==3.5f&&ps.Qw==1f,"PlayerState chega íntegro");
var join=Next(a,PacketType.PlayerJoin);
Check(join.Type==PacketType.PlayerJoin,"cliente recebe PlayerJoin");
b.Dispose();
var leave=Next(host,PacketType.PlayerLeave);
Check(leave.Type==PacketType.PlayerLeave&&leave.Id==w2.Id,"host detecta saída de cliente");
host.Dispose(); a.Dispose(); bad.Dispose();
Console.WriteLine(fails==0?"TODOS OS TESTES PASSARAM":"FALHAS: "+fails);
Environment.Exit(fails);
