using AADResearch.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AADResearch.Managers
{
    public class APIWorker
    {
        private const int API_WORKER_DELAY = 60000;

        private readonly APIWrapper APIWrapper;

        private CancellationTokenSource _ctsAPIWorker;

        public APIWorker(APIWrapper apiWrapper)
        {
            this.APIWrapper = apiWrapper;

            _ctsAPIWorker = new CancellationTokenSource();

           Task.Run(async() => { await RunApiWorkerAsync(_ctsAPIWorker.Token); });
        }

        private async Task RunApiWorkerAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    List<VolumeData> volumeDataList = await APIWrapper.GetVolumeData();

                    Thread.Sleep(API_WORKER_DELAY);
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception e)
            {

            }
        }
    }
}
