using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using AxPeg.Services.Interfaces;
using AxPeg.Dtos.Request;
using AxPeg.Dtos.Response;

namespace AxPeg.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SBPegRestController : ControllerBase
    {
        private readonly IAxPegService _pegService;
        private readonly IAxPegActionsService _actionsService;

        public SBPegRestController(IAxPegService pegService, IAxPegActionsService actionsService)
        {
            _pegService = pegService;
            _actionsService = actionsService;
        }

        [HttpPost("CanInitiate")]
        public async Task<IActionResult> CanInitiate([FromBody] InitiateRequest request)
        {
            bool canInitiate = await _pegService.CanInitiatePEGAsync(request.AppName, request.ProcessName, request.TaskName, request.IndexNo, request.KeyValue);
            if (canInitiate)
            {
                return Ok(ServiceResponse<bool>.Ok(true, "Can initiate PEG task."));
            }
            return BadRequest(ServiceResponse<bool>.Fail("PEG task cannot be initiated (Active record already exists)."));
        }

        [HttpPost("Approve")]
        public async Task<IActionResult> Approve([FromBody] ActionRequest request)
        {
            bool success = await _actionsService.ApproveTaskAsync(request.AppName, request.TaskId, request.UserName, request.Comments);
            if (success)
            {
                return Ok(ServiceResponse<bool>.Ok(true, "Task approved successfully."));
            }
            return BadRequest(ServiceResponse<bool>.Fail("Failed to approve task."));
        }

        [HttpPost("Reject")]
        public async Task<IActionResult> Reject([FromBody] ActionRequest request)
        {
            bool success = await _actionsService.RejectTaskAsync(request.AppName, request.TaskId, request.UserName, request.Comments);
            if (success)
            {
                return Ok(ServiceResponse<bool>.Ok(true, "Task rejected successfully."));
            }
            return BadRequest(ServiceResponse<bool>.Fail("Failed to reject task."));
        }

        [HttpPost("Forward")]
        public async Task<IActionResult> Forward([FromBody] ActionRequest request)
        {
            bool success = await _actionsService.ForwardTaskAsync(request.AppName, request.TaskId, request.UserName, request.ForwardToUser, request.Comments);
            if (success)
            {
                return Ok(ServiceResponse<bool>.Ok(true, "Task forwarded successfully."));
            }
            return BadRequest(ServiceResponse<bool>.Fail("Failed to forward task."));
        }

        [HttpPost("Return")]
        public async Task<IActionResult> Return([FromBody] ActionRequest request)
        {
            bool success = await _actionsService.ReturnTaskAsync(request.AppName, request.TaskId, request.UserName, request.Comments);
            if (success)
            {
                return Ok(ServiceResponse<bool>.Ok(true, "Task returned successfully."));
            }
            return BadRequest(ServiceResponse<bool>.Fail("Failed to return task."));
        }

        [HttpGet("IsV2Process")]
        public async Task<IActionResult> IsV2Process([FromQuery] string appName, [FromQuery] string processName)
        {
            bool isV2 = await _pegService.IsPEGV2ProcessAsync(appName, processName);
            return Ok(ServiceResponse<bool>.Ok(isV2, $"Checked process version. V2: {isV2}"));
        }
    }
}
