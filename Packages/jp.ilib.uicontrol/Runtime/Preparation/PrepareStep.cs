using System.Threading.Tasks;
using UnityEngine;

namespace UIControl
{
	public abstract class PrepareStep : MonoBehaviour
	{
		public abstract Task ExecuteAsync(PrepareContext context);
	}
}
