
(function ($) {

	var App = {
		video: null,
		capturesContainer: null,
		intervalMS: 2500,

		init: function() {
			if (!this.isBrowserSupported()) {
				this.showBrowserNotSupportedMessage();
				return;
			}

			this.initDOM();
			this.initCamera();
		},

		isBrowserSupported: function() {
			return !!navigator && !!navigator.webkitGetUserMedia;
		},

		showBrowserNotSupportedMessage: function() {
			$('body *').remove();
			$('body').append($('<h1>', {
				text: 'Please use Chrome, as it\'s a cooler browser.'
			}));
		},

		initDOM: function() {
			this.video = document.getElementById('capture-video');
			this.capturesContainer = document.getElementById('captures-container');

			$('#upload-button').click(this.uploadImages.bind(this));
			$('#capture-button').click(this.captueImage.bind(this));
		},

		initCamera: function() {
			var self = this;
			navigator.webkitGetUserMedia({
				video: true
			}, function (stream) {
        video.src = window.URL.createObjectURL(stream);
        setupCapture();
      }, function (e) {
          console.log(e);
      });

			setInterval(self.updateIdentifications.bind(self), self.intervalMS);
		},

		updateIdentifications: function() {
			var canvas = document.createElement("canvas");
			canvas.width = this.video.clientWidth / 2;
			canvas.height = this.video.clientHeight / 2;
			var canvasCtx = canvas.getContext('2d');
			canvasCtx.drawImage(this.video, 0, 0, canvas.width, canvas.height);

			$.ajax({
				type: 'POST',
				url: '/api/face/detect',
				data: { '': this.canvasToBase64(canvas) }
			}).then(function (data) {
			  var $predictQuestion = $('#predict-question');
			  var $predictImg = $('#predict-image');
			  var $predictName = $('#predict-name');

				if (data && data.length) {
				  var name = data[0].Name;
					$predictImg.attr('src', "/api/face/image/" + name).addClass('known');
					$predictName.text(name);
					$predictQuestion.text('Är detta du?');
				} else {
				  $predictImg.attr('src', '/Content/unknown.png').removeClass('known');
				  $predictName.text('');
				  $predictQuestion.text('Vem är du?');
				}
			});
		},


		captureImage: function() {
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

			var canvas = document.createElement("canvas");
			canvas.width = video.clientWidth / 2;
			canvas.height = video.clientHeight / 2;
			var captureCtx = canvas.getContext('2d');
			captureCtx.drawImage(video, 0, 0, canvas.width, canvas.height);
			p1.appendChild(canvas);

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
		},


		uploadImages: function() {
			var canvases = capturesContainer.querySelectorAll("canvas");

			var waitHandles = [];
			for (var i = 0; i < canvases.length; i++) {
				var canvas = canvases[i];
				if (!canvas.parentElement.parentElement.classList.contains("excluded")) {
					waitHandles.push($.ajax({
						type: 'POST',
						url: '/api/face/learn/' + document.getElementById("user-name").value,
						data: { '': this.canvasToBase64(canvas) }
					}));
				}
			}

			$.when.apply($, waitHandles)
				.then(function () {
					alert("done uploading");
				});
		},


		canvasToBase64: function(canvas) {
		    var dataurl = canvas.toDataURL('image/jpg');
		    var stripped = dataurl.substring(22);
		    return stripped;
		}
	};


	App.init();
	

})(jQuery);