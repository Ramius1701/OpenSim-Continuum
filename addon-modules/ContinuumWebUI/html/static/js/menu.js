$(function(){
	// References
	var topmenu = $("#topmenu li");
	var members = $("#member-actions li");
	var loading = $("#loading");
	var main_content = $("#main_content");

	// The inherited templates mix Bootstrap generations and dynamically render
	// menu items. Own dropdown state here so it behaves consistently in desktop
	// browsers and viewer-embedded Chromium.
	var dropdownToggles = $("#topmenu > li.dropdown > .dropdown-toggle");
	dropdownToggles.on("click.continuum", function(event){
		event.preventDefault();
		event.stopPropagation();
		var parent = $(this).parent();
		var opening = !parent.hasClass("show");
		$("#topmenu > li.dropdown.show").removeClass("show")
			.children(".dropdown-menu").removeClass("show");
		$("#topmenu > li.dropdown > .dropdown-toggle").attr("aria-expanded", "false");
		if (opening) {
			parent.addClass("show");
			parent.children(".dropdown-menu").addClass("show");
			$(this).attr("aria-expanded", "true");
		}
	});
	$(document).on("click.continuumDropdown", function(){
		$("#topmenu > li.dropdown.show").removeClass("show")
			.children(".dropdown-menu").removeClass("show");
		dropdownToggles.attr("aria-expanded", "false");
	});
	$("#topmenu > li.dropdown > .dropdown-menu > li").on("click.continuumItem", function(event){
		event.preventDefault();
		event.stopImmediatePropagation();
		var pageId = this.id;
		$(this).closest("li.dropdown").removeClass("show")
			.children(".dropdown-menu").removeClass("show");
		dropdownToggles.attr("aria-expanded", "false");
		showLoading();
		loadcontent(pageId);
	});

	// Main menu click events
	topmenu.not('.dropdown').click(function(event){
		showLoading();
		switch(this.id){
			{MenuItemsArrayBegin}
			case "{MenuItemID}":
		    main_content.load("{MenuItemLocation}" + window.location.search, hideLoading);
				/* main_content.slideUp('swing',  function() {
				    main_content.load("{MenuItemLocation}" + window.location.search, hideLoading);
				    main_content.slideDown();
				}); */

				break;
				{ChildrenMenuItemsArrayBegin}
			case "{ChildMenuItemID}":
				main_content.load("{ChildMenuItemLocation}" + window.location.search, hideLoading);
				break;
				{ChildrenMenuItemsArrayEnd}
				{MenuItemsArrayEnd}
			default:
				// hide loading bar if there is no selected section
				hideLoading();
				break;
		}
		event.stopPropagation();
	});

	// member action events - login
	members.click(function(event){
		//show the loading bar
		//showLoading();
		//load selected section
		switch(this.id){
		 	{MenuItemsArrayBegin}
			case "{MenuItemID}":
		    main_content.load("{MenuItemLocation}" + window.location.search, hideLoading);
				/* main_content.slideUp('swing',  function() {
				    main_content.load("{MenuItemLocation}" + window.location.search, hideLoading);
				    main_content.slideDown();
				}); */

				break;
				{ChildrenMenuItemsArrayBegin}
			case "{ChildMenuItemID}":
				main_content.load("{ChildMenuItemLocation}" + window.location.search, hideLoading);
				break;
				{ChildrenMenuItemsArrayEnd}
				{MenuItemsArrayEnd}
			default:
				//hide loading bar if there is no selected section
				//hideLoading();
				break;
		}
		event.stopPropagation();
	});

	//show loading bar
	function showLoading(){
		loading
			.css({visibility:"visible"})
			.css({opacity:"1"})
			.css({display:"block"})
		;
	}
	//hide loading bar
	function hideLoading(){
		loading.fadeTo(1000, 0);
	};
});

// embedded page content
function continuumPageUrl(location, params=''){
	var query = [];
	params = (params || '').replace(/^[?&]+/, '');
	var existing = (window.location.search || '').replace(/^\?/, '');
	if (params !== '') query.push(params);
	if (existing !== '') query.push(existing);
	return location + (query.length ? '?' + query.join('&') : '');
};

function loadcontent(pageid, params=''){
	var main_content = $("#main_content");
	//load selected page
	switch(pageid){
		{MenuItemsArrayBegin}
			case "{MenuItemID}":
			main_content.load(continuumPageUrl("{MenuItemLocation}", params));
				break;
			{ChildrenMenuItemsArrayBegin}
			case "{ChildMenuItemID}":
				main_content.load(continuumPageUrl("{ChildMenuItemLocation}", params));
				break;
			{ChildrenMenuItemsArrayEnd}
		{MenuItemsArrayEnd}
		default:
			break;
	}
};

// WhiteCore account and administration fragments use this legacy function
// name. Continuum renders those fragments in the main shell, so keep one
// navigation implementation and route the legacy calls through it.
function loadusercontent(pageid, params=''){
	loadcontent(pageid, params);
};

function loadmodalcontent(pageid, params='', title='', static=false){
	var modal_content = $("#modalcontent");
	modal_content.html('');		// clear anything previously loaded

	//load selected page
	switch(pageid){
		{ModalItemsArrayBegin}
			case "{MenuItemID}":
		    modal_content.load(continuumPageUrl("{MenuItemLocation}", params));
				break;
		{ModalItemsArrayEnd}
		default:
			break;
	}

  if (static) {
	  $("#profileModal").modal({
	    backdrop: "static",
	    keyboard: false
	  });
	}
	$("#profileModal").modal("show");
};
