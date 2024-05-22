using System;
using System.Collections.Generic;
 
using UnityEngine;

namespace EnhancedValheimVRM
{
	public class MToonController : MonoBehaviour
	{
		class MatColor
		{
			public Material mat;
			public Color color;
			public Color shadeColor;
			public Color emission;
			public bool hasColor;
			public bool hasShadeColor;
			public bool hasEmission;
		}

		//private int _SunFogColor;
		private int _sunColor;
		private int _ambientColor;

		private List<MatColor> _matColors = new List<MatColor>();

		void Awake()
		{
			//_SunFogColor = Shader.PropertyToID("_SunFogColor");
			_sunColor = Shader.PropertyToID("_SunColor");
			_ambientColor = Shader.PropertyToID("_AmbientColor");
		}

		public void Setup(GameObject vrm)
		{
			_matColors.Clear();
			foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
			{
				foreach (var mat in smr.materials)
				{
					if (!_matColors.Exists(m => m.mat == mat))
					{
						_matColors.Add(new MatColor()
						{
							mat = mat,
							color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white,
							shadeColor = mat.HasProperty("_ShadeColor") ? mat.GetColor("_ShadeColor") : Color.white,
							emission = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black,
							hasColor = mat.HasProperty("_Color"),
							hasShadeColor = mat.HasProperty("_ShadeColor"),
							hasEmission = mat.HasProperty("_EmissionColor"),
						});
					}
				}
			}
		}

		void Update()
		{
			//var fog = Shader.GetGlobalColor(_SunFogColor);
			var sun = Shader.GetGlobalColor(_sunColor);
			var amb = Shader.GetGlobalColor(_ambientColor);
			var sunAmb = sun + amb;
			if (sunAmb.maxColorComponent > 0.7f) sunAmb /= 0.3f + sunAmb.maxColorComponent;

			foreach (var matColor in _matColors)
			{
				var col = matColor.color * sunAmb;
				col.a = matColor.color.a;
				if (col.maxColorComponent > 1.0f) col /= col.maxColorComponent;

				var shadeCol = matColor.shadeColor * sunAmb;
				shadeCol.a = matColor.shadeColor.a;
				if (shadeCol.maxColorComponent > 1.0f) shadeCol /= shadeCol.maxColorComponent;

				var emi = matColor.emission * sunAmb.grayscale;

				if (matColor.hasColor) matColor.mat.SetColor("_Color", col);
				if (matColor.hasShadeColor) matColor.mat.SetColor("_ShadeColor", shadeCol);
				if (matColor.hasEmission) matColor.mat.SetColor("_EmissionColor", emi);
			}
		}
	}
}
