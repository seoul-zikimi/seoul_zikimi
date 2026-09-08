// 공용 내비게이션 · 푸터 · 스크롤 등장
(function(){
  var page=document.body.dataset.page||'';
  var links=[['index.html','게임 소개','home'],['video.html','영상 · 스크린샷','video'],['download.html','빌드 다운로드','download'],['team.html','팀 소개','team']];
  var nav=document.createElement('nav');nav.className='top';
  nav.innerHTML='<div class="wrap"><a class="brand" href="index.html"><i></i>건축 레인저: 서울</a>'+
    '<button class="menu" aria-label="메뉴 열기" aria-expanded="false">☰</button><ul>'+
    links.map(function(l){return '<li><a href="'+l[0]+'"'+(l[2]===page?' aria-current="page"':'')+'>'+l[1]+'</a></li>'}).join('')+
    '</ul></div>';
  document.body.insertBefore(nav,document.body.firstChild);
  var btn=nav.querySelector('.menu'),ul=nav.querySelector('ul');
  btn.addEventListener('click',function(){var o=ul.classList.toggle('open');btn.setAttribute('aria-expanded',o)});

  var f=document.createElement('footer');
  f.innerHTML='<div class="wrap"><div><b>건축 레인저: 서울</b><br>팀 서울지키미 · Unity 6 · 제1회 서울 플레이업 AI 게임 챌린지</div>'+
    '<ul>'+links.map(function(l){return '<li><a href="'+l[0]+'">'+l[1]+'</a></li>'}).join('')+'<li><a href="mailto:jikimiseoul@gmail.com">문의</a></li></ul>'+
    '<div>© 2026 Team Seoul Zikimi</div></div>';
  document.body.appendChild(f);

  var els=document.querySelectorAll('.rv');
  if(location.search.indexOf('static')>-1||!('IntersectionObserver' in window)){els.forEach(function(e){e.classList.add('in')});return}
  var io=new IntersectionObserver(function(es){es.forEach(function(e){if(e.isIntersecting){e.target.classList.add('in');io.unobserve(e.target)}})},{threshold:.1});
  els.forEach(function(e){io.observe(e)});
})();

// 갤러리 태그 필터 (video.html) — URL 해시(#경복궁)로 초기 선택 가능
(function(){
  var bar=document.querySelector('.filters');if(!bar)return;
  var items=Array.prototype.slice.call(document.querySelectorAll('.gallery .g'));
  var btns=Array.prototype.slice.call(bar.querySelectorAll('button'));
  var empty=document.querySelector('.g-empty');
  function tags(el){return (el.dataset.tags||'').split(/\s+/)}
  btns.forEach(function(b){
    var f=b.dataset.filter,n=f==='all'?items.length:items.filter(function(i){return tags(i).indexOf(f)>-1}).length;
    var s=document.createElement('small');s.textContent=n;b.appendChild(s);
    b.addEventListener('click',function(){apply(f);if(history.replaceState)history.replaceState(null,'',f==='all'?location.pathname:'#'+f)});
  });
  function apply(f){
    var shown=0;
    btns.forEach(function(b){b.setAttribute('aria-pressed',b.dataset.filter===f)});
    items.forEach(function(i){var on=f==='all'||tags(i).indexOf(f)>-1;i.hidden=!on;if(on)shown++;
      var v=i.querySelector('video');if(v){if(on)v.play&&v.play().catch(function(){});else v.pause()}});
    if(empty)empty.hidden=shown>0;
  }
  var h=decodeURIComponent(location.hash.replace('#',''));
  if(h&&btns.some(function(b){return b.dataset.filter===h}))apply(h);
})();
