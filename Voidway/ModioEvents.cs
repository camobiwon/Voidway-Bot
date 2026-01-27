using Modio;
using Modio.Models;

namespace Voidway;

public static class ModioEvents
{
    static async void FetchLoop(Client)
    {
        while (true)
        {
            await Task.Delay(5000);
            try
            {
                

            }
            catch (Exception ex)
            {
                Logger.Warn()
            }
        }
        
    }
}