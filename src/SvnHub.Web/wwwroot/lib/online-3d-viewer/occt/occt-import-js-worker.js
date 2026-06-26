const occtBasePath = '/lib/online-3d-viewer/occt/';

importScripts (occtBasePath + 'occt-import-js.js');

onmessage = async function (ev)
{
	let modulOverrides = {
		locateFile: function (path) {
			return occtBasePath + path;
		}
	};
	let occt = await occtimportjs (modulOverrides);
	let result = occt.ReadFile (ev.data.format, ev.data.buffer, ev.data.params);
	postMessage (result);
};
