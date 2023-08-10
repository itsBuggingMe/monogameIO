using ServerClient;
namespace gameServer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            server server = new server(14242, 1);
            server.start();
        }
    }
}