
(function () {

	var video = document.getElementById("capture-video");

	var capturesContainer = document.getElementById("captures-container");

	function updateIdentifications() {

		var canvas = document.createElement("canvas");
		canvas.width = video.clientWidth / 2;
		canvas.height = video.clientHeight / 2;
		var canvasCtx = canvas.getContext('2d');
		canvasCtx.drawImage(video, 0, 0, canvas.width, canvas.height);

		$.ajax({
			type: 'POST',
			url: '/api/face/detect',
			data: { '': canvas.toDataURL('image/jpeg') }
		})
		.then(function (data) {
			var predictImg = document.getElementById("predict-img");
			var predictName = document.getElementById("predict-name");

			if (data.length) {
				var name = data[0].Name;
				predictImg.src = "/api/face/image/" + name;
				predictName.innerText = name;
			} else {
				predictImg.src = null;
				predictName.innerText = '';
			}

		});
	}

	setInterval(updateIdentifications, 5000);

	function setupCapture() {
		var captureButton = document.getElementById("capture-button");
		captureButton.addEventListener("click", function () {
			var rows = capturesContainer.querySelectorAll(".row");

			var row;
			if (rows.length === 0 || rows[rows.length - 1].querySelectorAll("col-md-3").length === 4) {
				row = document.createElement("div");
				row.classList.add("row");
				capturesContainer.appendChild(row);
			} else {
				row = rows[rows.length - 1];
			}

			var captureCell = document.createElement("div");
			captureCell.classList.add("col-xs-3");

			var p1 = document.createElement("p");
			captureCell.appendChild(p1);

			var capture = document.createElement("canvas");
			capture.width = video.clientWidth / 2;
			capture.height = video.clientHeight / 2;
			var captureCtx = capture.getContext('2d');
			captureCtx.drawImage(video, 0, 0, capture.width, capture.height);
			p1.appendChild(capture);

			var p2 = document.createElement("p");
			captureCell.appendChild(p2);

			var btn = document.createElement("button");
			btn.classList.add("btn");
			btn.value = "Exclude";
			btn.addEventListener("click", function () {
				if (captureCell.classList.contains("excluded")) {
					captureCell.classList.remove("excluded");
				} else {
					captureCell.classList.add("excluded");
				}
			});
			p2.appendChild(btn);
			

			row.appendChild(captureCell);
		});

		var uploadButton = document.getElementById("upload-button");
		uploadButton.addEventListener("click", function () {
			var canvases = capturesContainer.querySelectorAll("canvas");

			var waitHandles = [];
			for (var i = 0; i < canvases.length; i++) {
				var canvas = canvases[i];
				if (!canvas.parentElement.parentElement.classList.contains("excluded")) {
					waitHandles.push($.ajax({
						type: 'POST',
						url: '/api/face/learn/' + document.getElementById("user-name").value,
						data: { '': canvas.toDataURL('image/jpeg') }
					}));
				}
			}

			$.when.apply($, waitHandles)
				.then(function () {
					// updateIdentifications();
					alert("done uploading");
				});
		});
	}

	navigator.webkitGetUserMedia(
	{ video: true },
	function (stream) {
		video.src = window.URL.createObjectURL(stream);

		setupCapture();
	},
	function (e) {
		console.log(e)
	});

})();