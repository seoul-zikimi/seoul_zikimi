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
